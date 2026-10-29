#include "AthWorldHost.h"
#include "AthHostSubsystem.h"
#include "AthProtocol.h"
#include "Engine/World.h"
#include "Engine/Engine.h"
#include "Engine/GameInstance.h"
#include "Engine/GameViewportClient.h"
#include "HAL/FileManager.h"
#include "Misc/App.h"
#include "Misc/EngineVersion.h"
#include "Misc/Paths.h"
#include "RHIGlobals.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UnrealClient.h"

namespace
{
    bool HasMultiplePieWorlds()
    {
        int32 Count = 0;
        if (GEngine)
            for (const FWorldContext& Context : GEngine->GetWorldContexts())
                if (Context.WorldType == EWorldType::PIE && ::IsValid(Context.World()) && ++Count > 1) return true;
        return false;
    }
}

FAthWorldHost::FAthWorldHost(UWorld* InWorld, int32 InPieInstance, FAthSession& InSession)
    : World(InWorld), PieInstance(InPieInstance), Session(InSession) {}

FAthWorldHost::~FAthWorldHost() { Shutdown(); }

void FAthWorldHost::Shutdown()
{
    check(IsInGameThread());
    for (const FPending& Operation : Pending)
        Session.CompleteAsync(TEXT("ath.fixture.async_increment"), Operation.Id, false, TEXT("world_invalidated"));
    Pending.Reset();
    if (!ScreenshotPath.IsEmpty() && FScreenshotRequest::GetFilename() == ScreenshotPath) FScreenshotRequest::Reset();
    ScreenshotPath.Reset();
}

FString FAthWorldHost::GetHostName() const { return FApp::GetProjectName(); }
bool FAthWorldHost::IsValid() const { return ::IsValid(World.Get()) && !World->bIsTearingDown; }
bool FAthWorldHost::IsReady() const { return IsValid() && World->HasBegunPlay(); }
UAthHostSubsystem* FAthWorldHost::GetSubsystem() const
{
    if (!IsValid()) return nullptr;
    UGameInstance* Instance = World->GetGameInstance();
    return ::IsValid(Instance) ? Instance->GetSubsystem<UAthHostSubsystem>() : nullptr;
}

TArray<FString> FAthWorldHost::GetCapabilities() const
{
    TArray<FString> Result = {TEXT("fixture"), TEXT("unreal.world"), TEXT("host_registration")};
    if (IsValid() && !HasMultiplePieWorlds() && !GUsingNullRHI && FApp::CanEverRender() && World->GetGameInstance() && World->GetGameInstance()->GetGameViewportClient())
        Result.Add(TEXT("screenshot"));
    return Result;
}

TArray<FString> FAthWorldHost::GetStateKeys() const
{
    TArray<FString> Keys = {TEXT("ath.fixture.ready"), TEXT("ath.fixture.counter"), TEXT("ath.fixture.active"),
        TEXT("ath.fixture.last_action"), TEXT("unreal.engine_version"), TEXT("unreal.play_mode"), TEXT("unreal.world_name"),
        TEXT("unreal.world_valid"), TEXT("unreal.actor_count"), TEXT("unreal.delta_seconds"), TEXT("unreal.session_ready"),
        TEXT("unreal.pie_instance"), TEXT("scene_name")};
    if (const UAthHostSubsystem* Host = GetSubsystem()) Keys.Append(Host->GetStateKeys());
    return Keys;
}

TArray<FString> FAthWorldHost::GetCommands() const
{
    TArray<FString> Commands = {TEXT("ath.fixture.increment"), TEXT("ath.fixture.reset"), TEXT("ath.fixture.set_active"),
        TEXT("ath.fixture.async_increment")};
    if (const UAthHostSubsystem* Host = GetSubsystem()) Commands.Append(Host->GetCommands());
    return Commands;
}

bool FAthWorldHost::TryGetState(const FString& Key, FString& Value) const
{
    check(IsInGameThread());
    if (!IsValid()) return false;
    auto Boolean = [](bool In) { return In ? TEXT("true") : TEXT("false"); };
    if (Key == TEXT("ath.fixture.ready") || Key == TEXT("unreal.session_ready")) Value = Boolean(IsReady());
    else if (Key == TEXT("ath.fixture.counter")) Value = FString::FromInt(Counter);
    else if (Key == TEXT("ath.fixture.active")) Value = Boolean(bActive);
    else if (Key == TEXT("ath.fixture.last_action")) Value = LastAction;
    else if (Key == TEXT("unreal.engine_version")) Value = FEngineVersion::Current().ToString();
    else if (Key == TEXT("unreal.world_valid")) Value = TEXT("true");
    else if (Key == TEXT("unreal.world_name") || Key == TEXT("scene_name")) Value = World->GetMapName();
    else if (Key == TEXT("unreal.play_mode")) Value = World->WorldType == EWorldType::PIE ? TEXT("PIE") : TEXT("Standalone");
    else if (Key == TEXT("unreal.pie_instance")) Value = FString::FromInt(PieInstance);
    else if (Key == TEXT("unreal.actor_count")) Value = FString::FromInt(World->GetActorCount());
    else if (Key == TEXT("unreal.delta_seconds")) Value = FString::SanitizeFloat(World->GetDeltaSeconds());
    else if (const UAthHostSubsystem* Host = GetSubsystem()) return Host->TryGetState(Key, Value);
    else return false;
    return true;
}

FAthCommandResult FAthWorldHost::Execute(const FString& Command, const TSharedRef<FJsonObject>& Arguments,
    const FString& CorrelationId)
{
    check(IsInGameThread());
    if (!IsValid()) return FAthCommandResult::Failure(TEXT("not_ready"));
    const TArray<TSharedPtr<FJsonValue>>* Args = nullptr;
    Arguments->TryGetArrayField(TEXT("args"), Args);
    auto FirstArg = [&]() { return Args && !Args->IsEmpty() ? (*Args)[0]->AsString() : FString(); };
    if (Command == TEXT("ath.fixture.increment"))
    {
        if (Counter == MAX_int32) return FAthCommandResult::Failure(TEXT("counter_overflow"));
        ++Counter; LastAction = TEXT("increment"); Session.ObserveEvent(TEXT("ath.fixture.changed"));
        return FAthCommandResult::Success(FString::FromInt(Counter));
    }
    if (Command == TEXT("ath.fixture.reset"))
    {
        Reset(TEXT("all"));
        Session.Reset(false);
        return FAthCommandResult::Success(TEXT("reset"));
    }
    if (Command == TEXT("ath.fixture.set_active"))
    {
        bool Value = false;
        if (!Arguments->TryGetBoolField(TEXT("active"), Value))
        {
            const FString Arg = FirstArg();
            if (Arg != TEXT("true") && Arg != TEXT("false")) return FAthCommandResult::Failure(TEXT("bad_arguments"));
            Value = Arg == TEXT("true");
        }
        bActive = Value; LastAction = TEXT("set_active"); Session.ObserveEvent(TEXT("ath.fixture.changed"));
        return FAthCommandResult::Success(Value ? TEXT("true") : TEXT("false"));
    }
    if (Command == TEXT("ath.fixture.async_increment"))
    {
        if (Pending.Num() >= 16) return FAthCommandResult::Failure(TEXT("busy"));
        double DelayMs = 50;
        if (!Arguments->TryGetNumberField(TEXT("delayMs"), DelayMs) && !FirstArg().IsEmpty())
        {
            if (!LexTryParseString(DelayMs, *FirstArg())) return FAthCommandResult::Failure(TEXT("bad_arguments"));
        }
        if (!FMath::IsFinite(DelayMs) || DelayMs < 0 || DelayMs > 30000) return FAthCommandResult::Failure(TEXT("bad_arguments"));
        Pending.Add({CorrelationId, FPlatformTime::Seconds() + DelayMs / 1000.0});
        FAthCommandResult Result = FAthCommandResult::Success(CorrelationId);
        Result.bDispatched = true;
        return Result;
    }
    if (UAthHostSubsystem* Host = GetSubsystem())
    {
        FString Json;
        FJsonSerializer::Serialize(Arguments, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json));
        return Host->Execute(Command, Json, CorrelationId);
    }
    return FAthCommandResult::Failure(TEXT("unknown_command"));
}

FAthCommandResult FAthWorldHost::Reset(const FString& Scope)
{
    check(IsInGameThread());
    if (Scope == TEXT("all"))
    {
        for (const FPending& Operation : Pending)
            Session.CompleteAsync(TEXT("ath.fixture.async_increment"), Operation.Id, false, TEXT("reset"));
        Pending.Reset(); Counter = 0; bActive = true; LastAction = TEXT("reset");
    }
    return FAthCommandResult::Success();
}

FAthCommandResult FAthWorldHost::CaptureScreenshot(const FString& Label)
{
    check(IsInGameThread());
    UGameInstance* Instance = IsValid() ? World->GetGameInstance() : nullptr;
    // FScreenshotRequest is engine-global; never claim selected-world evidence when several PIE worlds exist.
    if (HasMultiplePieWorlds()) return FAthCommandResult::Failure(TEXT("multiple_pie_worlds"));
    UGameViewportClient* Viewport = ::IsValid(Instance) ? Instance->GetGameViewportClient() : nullptr;
    if (GUsingNullRHI || !FApp::CanEverRender() || !::IsValid(Viewport) || !Viewport->Viewport)
        return FAthCommandResult::Failure(TEXT("rendering_unavailable"));
    if (!ScreenshotPath.IsEmpty() || FScreenshotRequest::IsScreenshotRequested()) return FAthCommandResult::Failure(TEXT("busy"));
    const FString Directory = FPaths::ConvertRelativePathToFull(FPaths::ProjectSavedDir() / TEXT("ATH/Screenshots"));
    if (!IFileManager::Get().MakeDirectory(*Directory, true)) return FAthCommandResult::Failure(TEXT("artifact_directory"));
    ScreenshotPath = Directory / (FAthProtocol::SanitizeLabel(Label) + TEXT("_") + FGuid::NewGuid().ToString(EGuidFormats::Digits) + TEXT(".png"));
    ScreenshotDeadline = FPlatformTime::Seconds() + 10.0;
    FScreenshotRequest::RequestScreenshot(ScreenshotPath, false, false, false, FIntRect(), true);
    return FAthCommandResult::Success(ScreenshotPath);
}

void FAthWorldHost::Tick()
{
    check(IsInGameThread());
    if (!IsValid()) return;
    const double Now = FPlatformTime::Seconds();
    for (int32 Index = Pending.Num() - 1; Index >= 0; --Index)
    {
        if (Pending[Index].Due > Now) continue;
        const bool bSuccess = Counter < MAX_int32;
        if (bSuccess) { ++Counter; LastAction = TEXT("async_increment"); Session.ObserveEvent(TEXT("ath.fixture.changed")); }
        Session.CompleteAsync(TEXT("ath.fixture.async_increment"), Pending[Index].Id, bSuccess,
            bSuccess ? TEXT("") : TEXT("counter_overflow"));
        Pending.RemoveAtSwap(Index);
    }
    if (!ScreenshotPath.IsEmpty() && (IFileManager::Get().FileSize(*ScreenshotPath) > 0 || Now >= ScreenshotDeadline))
    {
        if (Now >= ScreenshotDeadline && FScreenshotRequest::GetFilename() == ScreenshotPath) FScreenshotRequest::Reset();
        ScreenshotPath.Reset();
    }
}
