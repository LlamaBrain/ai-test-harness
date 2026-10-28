#include "AthRuntime.h"
#include "IAthHost.h"
#include "Modules/ModuleManager.h"

#if !UE_BUILD_SHIPPING && !UE_BUILD_TEST
#include "AthDispatcher.h"
#include "AthSession.h"
#include "AthTransport.h"
#include "AthWorldHost.h"
#include "Containers/Ticker.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "HAL/IConsoleManager.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"

DEFINE_LOG_CATEGORY_STATIC(LogAth, Log, All);

class FAthRuntimeModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override
    {
        check(IsInGameThread());
        Instance = this;
        if (!FParse::Param(FCommandLine::Get(), TEXT("ath-remote-console"))) return;
        int32 Port = 8787;
        FParse::Value(FCommandLine::Get(), TEXT("ath-port="), Port);
        FParse::Value(FCommandLine::Get(), TEXT("ath-pie-instance="), SelectedPieInstance);
        Dispatcher = MakeUnique<FAthDispatcher>(Session, [this]() { return GetHost(); });
        if (!Transport.Start(Port, [this](const FString& Command) { return Dispatcher->Execute(Command); }))
        {
            UE_LOG(LogAth, Error, TEXT("ATH failed to bind loopback port %d"), Port);
            Dispatcher.Reset();
            return;
        }
        CleanupHandle = FWorldDelegates::OnWorldCleanup.AddRaw(this, &FAthRuntimeModule::OnWorldCleanup);
        TickerHandle = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateRaw(this, &FAthRuntimeModule::Tick));
        StatusCommand = IConsoleManager::Get().RegisterConsoleCommand(TEXT("ath.status"), TEXT("Report ATH listener status"),
            FConsoleCommandDelegate::CreateLambda([this]() { UE_LOG(LogAth, Display, TEXT("ATH loopback active=%s"), Transport.IsActive() ? TEXT("true") : TEXT("false")); }),
            ECVF_Default);
        UE_LOG(LogAth, Display, TEXT("ATH initialized: loopback 127.0.0.1:%d; game-thread execution; PIE instance=%d"), Port, SelectedPieInstance);
    }

    virtual void ShutdownModule() override
    {
        check(IsInGameThread());
        if (StatusCommand) IConsoleManager::Get().UnregisterConsoleObject(StatusCommand);
        StatusCommand = nullptr;
        FTSTicker::RemoveTicker(TickerHandle);
        TickerHandle.Reset();
        FWorldDelegates::OnWorldCleanup.Remove(CleanupHandle);
        Transport.Stop();
        ShutdownRegisteredHost();
        RegisteredHost.Reset();
        WorldHost.Reset();
        Session.Reset(true);
        Dispatcher.Reset();
        Instance = nullptr;
        UE_LOG(LogAth, Display, TEXT("ATH shutdown complete"));
    }

    static FAthRuntimeModule* Instance;
    FAthSession Session;
    TSharedPtr<IAthHost> RegisteredHost;
    FAthTransport Transport;
    void ResetDefaultHost()
    {
        Transport.DisconnectClients();
        WorldHost.Reset();
    }
    void ShutdownRegisteredHost()
    {
        TSharedPtr<IAthHost> Previous = MoveTemp(RegisteredHost);
        if (Previous) Previous->Shutdown();
    }

private:
    TSharedPtr<FAthWorldHost> WorldHost;
    TUniquePtr<FAthDispatcher> Dispatcher;
    FTSTicker::FDelegateHandle TickerHandle;
    FDelegateHandle CleanupHandle;
    IConsoleObject* StatusCommand = nullptr;
    int32 SelectedPieInstance = INDEX_NONE;

    UWorld* SelectWorld(int32& OutPieInstance) const
    {
        OutPieInstance = INDEX_NONE;
        if (!GEngine) return nullptr;
        UWorld* Result = nullptr;
        for (const FWorldContext& Context : GEngine->GetWorldContexts())
        {
            UWorld* World = Context.World();
            if (!::IsValid(World) || World->bIsTearingDown) continue;
            if (Context.WorldType == EWorldType::PIE)
            {
                if (SelectedPieInstance != INDEX_NONE && Context.PIEInstance != SelectedPieInstance) continue;
                if (OutPieInstance == INDEX_NONE || Context.PIEInstance < OutPieInstance)
                { Result = World; OutPieInstance = Context.PIEInstance; }
            }
            else if (Context.WorldType == EWorldType::Game && SelectedPieInstance == INDEX_NONE && !Result) Result = World;
        }
        return Result;
    }

    void UpdateWorld()
    {
        int32 PieInstance;
        UWorld* World = SelectWorld(PieInstance);
        if (WorldHost && (!WorldHost->IsValid() || WorldHost->GetWorld() != World))
        {
            Transport.DisconnectClients();
            WorldHost.Reset();
            ShutdownRegisteredHost();
            RegisteredHost.Reset();
            Session.Reset(true);
            UE_LOG(LogAth, Display, TEXT("ATH world invalidated; fixture and pending observations cleared"));
        }
        if (!WorldHost && World)
        {
            WorldHost = MakeShared<FAthWorldHost>(World, PieInstance, Session);
            UE_LOG(LogAth, Display, TEXT("ATH selected world %s PIE=%d"), *World->GetName(), PieInstance);
        }
        if (RegisteredHost && !RegisteredHost->IsValid())
        {
            ShutdownRegisteredHost();
            RegisteredHost.Reset();
            Session.Reset(true);
        }
    }

    TSharedPtr<IAthHost> GetHost()
    {
        UpdateWorld();
        return RegisteredHost ? RegisteredHost : StaticCastSharedPtr<IAthHost>(WorldHost);
    }

    bool Tick(float DeltaSeconds)
    {
        check(IsInGameThread());
        UpdateWorld();
        if (WorldHost) WorldHost->Tick();
        Transport.Tick();
        return true;
    }

    void OnWorldCleanup(UWorld* World, bool bSessionEnded, bool bCleanupResources)
    {
        check(IsInGameThread());
        if (WorldHost && WorldHost->GetWorld() == World)
        {
            Transport.DisconnectClients();
            WorldHost.Reset();
            ShutdownRegisteredHost();
            RegisteredHost.Reset();
            Session.Reset(true);
            UE_LOG(LogAth, Display, TEXT("ATH world cleanup: clients disconnected; session observations cleared"));
        }
    }
};

FAthRuntimeModule* FAthRuntimeModule::Instance = nullptr;
IMPLEMENT_MODULE(FAthRuntimeModule, AthRuntime)

void FAthRuntime::RegisterHost(const TSharedRef<IAthHost>& Host)
{
    check(IsInGameThread());
    if (FAthRuntimeModule* Module = FAthRuntimeModule::Instance)
    {
        if (Module->RegisteredHost) Module->ShutdownRegisteredHost();
        Module->ResetDefaultHost();
        Module->RegisteredHost.Reset();
        Module->Session.Reset(true);
        Module->RegisteredHost = Host;
    }
}
void FAthRuntime::UnregisterHost()
{
    check(IsInGameThread());
    if (FAthRuntimeModule* Module = FAthRuntimeModule::Instance)
    {
        if (Module->RegisteredHost) Module->ShutdownRegisteredHost();
        Module->ResetDefaultHost();
        Module->RegisteredHost.Reset(); Module->Session.Reset(true);
    }
}
void FAthRuntime::ObserveEvent(const FString& EventName)
{
    check(IsInGameThread());
    if (FAthRuntimeModule* Module = FAthRuntimeModule::Instance)
        if (Module->Transport.IsActive()) Module->Session.ObserveEvent(EventName);
}
void FAthRuntime::CompleteAsync(const FString& Name, const FString& CorrelationId, bool bSuccess, const FString& Error)
{
    check(IsInGameThread());
    if (FAthRuntimeModule* Module = FAthRuntimeModule::Instance)
        if (Module->Transport.IsActive()) Module->Session.CompleteAsync(Name, CorrelationId, bSuccess, Error);
}
bool FAthRuntime::IsRemoteActive()
{
    return FAthRuntimeModule::Instance && FAthRuntimeModule::Instance->Transport.IsActive();
}

uint64 FAthRuntime::GetSessionGeneration()
{
    check(IsInGameThread());
    return FAthRuntimeModule::Instance ? FAthRuntimeModule::Instance->Session.GetGeneration() : 0;
}
void FAthRuntime::ObserveEventForSession(uint64 Generation, const FString& EventName)
{
    check(IsInGameThread());
    if (Generation != 0 && Generation == GetSessionGeneration()) ObserveEvent(EventName);
}
void FAthRuntime::CompleteAsyncForSession(uint64 Generation, const FString& Name, const FString& CorrelationId, bool bSuccess, const FString& Error)
{
    check(IsInGameThread());
    if (Generation != 0 && Generation == GetSessionGeneration()) CompleteAsync(Name, CorrelationId, bSuccess, Error);
}
#else
IMPLEMENT_MODULE(FDefaultModuleImpl, AthRuntime)
void FAthRuntime::RegisterHost(const TSharedRef<IAthHost>& Host) {}
void FAthRuntime::UnregisterHost() {}
void FAthRuntime::ObserveEvent(const FString& EventName) {}
void FAthRuntime::CompleteAsync(const FString& Name, const FString& CorrelationId, bool bSuccess, const FString& Error) {}
bool FAthRuntime::IsRemoteActive() { return false; }
uint64 FAthRuntime::GetSessionGeneration() { return 0; }
void FAthRuntime::ObserveEventForSession(uint64 Generation, const FString& EventName) {}
void FAthRuntime::CompleteAsyncForSession(uint64 Generation, const FString& Name, const FString& CorrelationId, bool bSuccess, const FString& Error) {}
#endif
