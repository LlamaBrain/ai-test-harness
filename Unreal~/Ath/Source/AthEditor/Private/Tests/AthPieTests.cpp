#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "AthRuntime.h"
#include "Editor.h"
#include "Engine/World.h"
#include "IPAddress.h"
#include "Misc/CommandLine.h"
#include "Misc/Parse.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "SocketSubsystem.h"
#include "Sockets.h"
#include "Tests/AutomationEditorCommon.h"

namespace
{
    class FWaitForAthPie final : public IAutomationLatentCommand
    {
    public:
        FWaitForAthPie(FAutomationTestBase* InTest, bool bInPlaying) : Test(InTest), bPlaying(bInPlaying) {}
        virtual bool Update() override
        {
            if (Deadline == 0) Deadline = FPlatformTime::Seconds() + 120;
            const bool bReady = GEditor && GEditor->PlayWorld && GEditor->PlayWorld->HasBegunPlay();
            if (bPlaying ? bReady : (GEditor && !GEditor->PlayWorld)) return true;
            if (FPlatformTime::Seconds() < Deadline) return false;
            Test->AddError(bPlaying ? TEXT("PIE did not start before deadline") : TEXT("PIE did not end before deadline"));
            return true;
        }
    private:
        FAutomationTestBase* Test;
        bool bPlaying;
        double Deadline = 0;
    };

    /** Actual TCP round trip, pumped across engine frames so the listener can execute on GameThread. */
    class FAthPieWireStep final : public IAutomationLatentCommand
    {
    public:
        FAthPieWireStep(FAutomationTestBase* InTest, TFunction<FString()> InRequest,
            TFunction<void(const TSharedPtr<FJsonObject>&)> InValidate)
            : Test(InTest), Request(MoveTemp(InRequest)), Validate(MoveTemp(InValidate)) {}
        virtual ~FAthPieWireStep() override
        {
            if (Socket) { Socket->Close(); ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->DestroySocket(Socket); }
        }
        virtual bool Update() override
        {
            if (Deadline == 0) Deadline = FPlatformTime::Seconds() + 30;
            if (FPlatformTime::Seconds() >= Deadline) { Test->AddError(TEXT("ATH PIE TCP request timed out: ") + Request()); return true; }
            if (!Socket)
            {
                ISocketSubsystem* Sockets = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
                if (!Sockets) { Test->AddError(TEXT("Socket subsystem unavailable")); return true; }
                Socket = Sockets->CreateSocket(NAME_Stream, TEXT("ATH PIE acceptance"), false);
                if (!Socket) { Test->AddError(TEXT("TCP socket creation failed")); return true; }
                Socket->SetNonBlocking(true);
                int32 Port = 8787;
                FParse::Value(FCommandLine::Get(), TEXT("ath-port="), Port);
                const TSharedRef<FInternetAddr> Address = Sockets->CreateInternetAddr();
                bool bValid;
                Address->SetIp(TEXT("127.0.0.1"), bValid); Address->SetPort(Port);
                Socket->Connect(*Address);
                const FString Wire = Request() + TEXT("\n");
                FTCHARToUTF8 Encoded(*Wire);
                Output.Append(reinterpret_cast<const uint8*>(Encoded.Get()), Encoded.Length());
                return false;
            }
            if (Sent < Output.Num())
            {
                int32 Count = 0;
                if (Socket->Send(Output.GetData() + Sent, Output.Num() - Sent, Count)) Sent += Count;
                return false;
            }
            uint32 Pending = 0;
            if (!Socket->HasPendingData(Pending) || !Pending) return false;
            uint8 Buffer[4096]; int32 Read = 0;
            if (!Socket->Recv(Buffer, FMath::Min<uint32>(Pending, sizeof(Buffer)), Read) || Read <= 0) return false;
            Input.Append(Buffer, Read);
            if (Input.Num() > 65536) { Test->AddError(TEXT("Oversized ATH response")); return true; }
            const int32 Newline = Input.Find(static_cast<uint8>('\n'));
            if (Newline == INDEX_NONE) return false;
            FUTF8ToTCHAR Decoded(reinterpret_cast<const ANSICHAR*>(Input.GetData()), Newline);
            TSharedPtr<FJsonObject> Reply;
            if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(FString(Decoded.Length(), Decoded.Get())), Reply) || !Reply)
                Test->AddError(TEXT("ATH response was not a JSON object"));
            else Validate(Reply);
            return true;
        }
    private:
        FAutomationTestBase* Test;
        TFunction<FString()> Request;
        TFunction<void(const TSharedPtr<FJsonObject>&)> Validate;
        double Deadline = 0;
        FSocket* Socket = nullptr;
        TArray<uint8> Input, Output;
        int32 Sent = 0;
    };
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthPieLifecycleTest, "ATH.Editor.PIERoundTripAndRestart",
    EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)
bool FAthPieLifecycleTest::RunTest(const FString& Parameters)
{
    if (!FAthRuntime::IsRemoteActive())
    {
        AddError(TEXT("PIE acceptance requires explicit -ath-remote-console; it never silently enables instrumentation"));
        return false;
    }
    const TSharedRef<FString> AsyncId = MakeShared<FString>();
    auto Wire = [this](const FString& Request, const FString& Expected)
    {
        ADD_LATENT_AUTOMATION_COMMAND(FAthPieWireStep(this, [Request]() { return Request; }, [this, Request, Expected](const TSharedPtr<FJsonObject>& Reply)
        {
            TestEqual(*(TEXT("Successful TCP response: ") + Request), Reply->GetStringField(TEXT("status")),
                FString(Expected == TEXT("[dispatched]") ? TEXT("dispatched") : TEXT("success")));
            const auto& Lines = Reply->GetArrayField(TEXT("lines"));
            TestTrue(*(TEXT("Terminal payload: ") + Request), !Lines.IsEmpty() && Lines.Last()->AsString().Contains(Expected));
        }));
    };
    ADD_LATENT_AUTOMATION_COMMAND(FStartPIECommand(false));
    ADD_LATENT_AUTOMATION_COMMAND(FWaitForAthPie(this, true));
    Wire(TEXT("harness.ping"), TEXT("pong=true"));
    Wire(TEXT("harness.capabilities"), TEXT("unreal"));
    Wire(TEXT("harness.state unreal.play_mode"), TEXT("value=\"PIE\""));
    Wire(TEXT("harness.state unreal.world_valid"), TEXT("value=\"true\""));
    Wire(TEXT("harness.state ath.fixture.ready"), TEXT("value=\"true\""));
    Wire(TEXT("ath.fixture.reset"), TEXT("OK:"));
    Wire(TEXT("ath.fixture.increment"), TEXT("value=\"1\""));
    Wire(TEXT("harness.state ath.fixture.counter"), TEXT("value=\"1\""));
    Wire(TEXT("harness.state event:ath.fixture.changed"), TEXT("value=\"true\""));
    Wire(TEXT("harness.reset"), TEXT("reset=true"));
    Wire(TEXT("harness.state event:ath.fixture.changed"), TEXT("value=\"false\""));
    ADD_LATENT_AUTOMATION_COMMAND(FAthPieWireStep(this, []() { return FString(TEXT("ath.fixture.async_increment 0")); },
        [this, AsyncId](const TSharedPtr<FJsonObject>& Reply)
        {
            TestEqual(TEXT("Async request dispatched"), Reply->GetStringField(TEXT("status")), FString(TEXT("dispatched")));
            *AsyncId = Reply->GetStringField(TEXT("correlationId"));
        }));
    ADD_LATENT_AUTOMATION_COMMAND(FAthPieWireStep(this, [AsyncId]() { return TEXT("harness.state async:") + *AsyncId; },
        [this](const TSharedPtr<FJsonObject>& Reply)
        {
            TestTrue(TEXT("Async completes through remote observation"), Reply->GetArrayField(TEXT("lines")).Last()->AsString().Contains(TEXT("value=\"done\"")));
        }));
    Wire(TEXT("ath.fixture.async_increment 30000"), TEXT("[dispatched]"));
    // This pending operation must never mutate the fresh session after teardown.
    ADD_LATENT_AUTOMATION_COMMAND(FEndPlayMapCommand());
    ADD_LATENT_AUTOMATION_COMMAND(FWaitForAthPie(this, false));
    Wire(TEXT("harness.state game_ready"), TEXT("status=not_ready"));
    ADD_LATENT_AUTOMATION_COMMAND(FStartPIECommand(false));
    ADD_LATENT_AUTOMATION_COMMAND(FWaitForAthPie(this, true));
    Wire(TEXT("harness.ping"), TEXT("pong=true"));
    Wire(TEXT("harness.state ath.fixture.counter"), TEXT("value=\"0\""));
    Wire(TEXT("harness.state event:ath.fixture.changed"), TEXT("value=\"false\""));
    ADD_LATENT_AUTOMATION_COMMAND(FEndPlayMapCommand());
    ADD_LATENT_AUTOMATION_COMMAND(FWaitForAthPie(this, false));
    return true;
}
#endif
