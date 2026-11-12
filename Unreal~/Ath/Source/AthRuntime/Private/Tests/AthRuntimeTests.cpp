#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "AthDispatcher.h"
#include "AthSession.h"
#include "AthTransport.h"
#include "AthWorldHost.h"
#include "AthRuntime.h"
#include "Engine/World.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace
{
    class FPermissiveAthHost final : public IAthHost
    {
    public:
        bool bCalled = false;
        bool bShutdown = false;
        virtual void Shutdown() override { bShutdown = true; }
        virtual FString GetHostName() const override { return TEXT("RegistrationTest"); }
        virtual bool IsReady() const override { return true; }
        virtual TArray<FString> GetCommands() const override { return {TEXT("registered.only")}; }
        virtual bool TryGetState(const FString& Key, FString& Value) const override { return false; }
        virtual FAthCommandResult Execute(const FString& Command, const TSharedRef<FJsonObject>& Arguments,
            const FString& CorrelationId) override
        {
            bCalled = true;
            return IsInGameThread() ? FAthCommandResult::Success() : FAthCommandResult::Failure(TEXT("wrong_thread"));
        }
    };
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthAdapterOwnershipTest, "ATH.Runtime.AdapterOwnershipAndGeneration",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthAdapterOwnershipTest::RunTest(const FString& Parameters)
{
    const TSharedRef<FPermissiveAthHost> OldHost = MakeShared<FPermissiveAthHost>();
    const TSharedRef<FPermissiveAthHost> NewHost = MakeShared<FPermissiveAthHost>();
    FAthRuntime::RegisterHost(OldHost);
    const uint64 OldGeneration = FAthRuntime::GetSessionGeneration();
    FAthRuntime::RegisterHost(NewHost);
    TestTrue(TEXT("Replacement shuts down retained old shared owner"), OldHost->bShutdown);
    TestFalse(TEXT("Replacement owner remains active"), NewHost->bShutdown);
    TestTrue(TEXT("Replacement advances callback generation"), FAthRuntime::GetSessionGeneration() > OldGeneration);
    // Guarded late callbacks are safe even when caller intentionally retains an old adapter.
    FAthRuntime::ObserveEventForSession(OldGeneration, TEXT("stale"));
    FAthRuntime::CompleteAsyncForSession(OldGeneration, TEXT("stale"), TEXT("old"), true);
    FAthRuntime::UnregisterHost();
    TestTrue(TEXT("Unregister shuts down retained owner"), NewHost->bShutdown);
    FAthRuntime::UnregisterHost();
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthNativeRegistrationTest, "ATH.Runtime.NativeCommandRegistration",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthNativeRegistrationTest::RunTest(const FString& Parameters)
{
    FAthSession Session;
    const TSharedRef<FPermissiveAthHost> Host = MakeShared<FPermissiveAthHost>();
    FAthDispatcher Dispatcher(Session, [Host]() { return TSharedPtr<IAthHost>(Host); });
    TSharedPtr<FJsonObject> Reply;
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.execute unregistered e30="))), Reply);
    TestEqual(TEXT("Unregistered execution rejected"), Reply->GetStringField(TEXT("status")), FString(TEXT("failed")));
    TestFalse(TEXT("Unregistered callback never invoked"), Host->bCalled);
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("unregistered"))), Reply);
    TestEqual(TEXT("Unregistered direct command rejected"), Reply->GetStringField(TEXT("status")), FString(TEXT("failed")));
    TestFalse(TEXT("Direct unregistered callback never invoked"), Host->bCalled);
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.execute registered.only e30="))), Reply);
    TestEqual(TEXT("Registered callback executes"), Reply->GetStringField(TEXT("status")), FString(TEXT("success")));
    TestTrue(TEXT("Registered callback was invoked"), Host->bCalled);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthWorldLifecycleTest, "ATH.Runtime.WorldInvalidationAndPendingOperations",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthWorldLifecycleTest::RunTest(const FString& Parameters)
{
    TestTrue(TEXT("Automation calls execute on game thread"), IsInGameThread());
    FAthSession Session;
    UWorld* World = UWorld::CreateWorld(EWorldType::Game, false, FName(TEXT("AthLifecycleTest")));
    if (!TestNotNull(TEXT("Native world created"), World)) return false;
    TSharedPtr<FAthWorldHost> Host = MakeShared<FAthWorldHost>(World, INDEX_NONE, Session);
    TestTrue(TEXT("Live UObject valid"), Host->IsValid());
    FString Value, Status;
    TestTrue(TEXT("Native world state exposed"), Host->TryGetState(TEXT("unreal.world_valid"), Value));
    TestEqual(TEXT("Native world state true"), Value, FString(TEXT("true")));
    TestTrue(TEXT("Actor count is actual Unreal state"), Host->TryGetState(TEXT("unreal.actor_count"), Value));
    TestEqual(TEXT("Actor count matches engine"), Value, FString::FromInt(World->GetActorCount()));
    TSharedRef<FJsonObject> Arguments = MakeShared<FJsonObject>();
    Arguments->SetNumberField(TEXT("delayMs"), 0);
    TestTrue(TEXT("Async is explicitly dispatched"), Host->Execute(TEXT("ath.fixture.async_increment"), Arguments, TEXT("complete")).bDispatched);
    Host->Tick();
    Session.TryGetState(TEXT("async:complete"), Status, Value);
    TestEqual(TEXT("Game-thread tick completes async"), Value, FString(TEXT("done")));
    Session.TryGetState(TEXT("event:ath.fixture.changed"), Status, Value);
    TestEqual(TEXT("Async event latched"), Value, FString(TEXT("true")));
    Host->TryGetState(TEXT("ath.fixture.counter"), Value);
    TestEqual(TEXT("Async mutation observed"), Value, FString(TEXT("1")));
    Arguments->SetNumberField(TEXT("delayMs"), 30000);
    Host->Execute(TEXT("ath.fixture.async_increment"), Arguments, TEXT("cancelled"));
    World->BeginTearingDown();
    TestFalse(TEXT("Tearing-down UObject rejected"), Host->IsValid());
    TestFalse(TEXT("Invalid world cannot mutate"), Host->Execute(TEXT("ath.fixture.increment"), Arguments, TEXT("invalid")).bSuccess);
    TestFalse(TEXT("Invalid world state does not dereference"), Host->TryGetState(TEXT("unreal.actor_count"), Value));
    Host.Reset();
    Session.TryGetState(TEXT("async_success:cancelled"), Status, Value);
    TestEqual(TEXT("Pending operation cancelled during host destruction"), Value, FString(TEXT("false")));
    Session.TryGetState(TEXT("last_async"), Status, Value);
    TestTrue(TEXT("Cancellation diagnostic retained before session reset"), Value.Contains(TEXT("world_invalidated")));
    Session.Reset(true);
    Session.TryGetState(TEXT("event:ath.fixture.changed"), Status, Value);
    TestEqual(TEXT("Replacement session cannot inherit event"), Value, FString(TEXT("false")));
    World->DestroyWorld(false);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthDispatcherTest, "ATH.Runtime.DispatcherErrorsAndCorrelation",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthDispatcherTest::RunTest(const FString& Parameters)
{
    FAthSession Session;
    TSharedPtr<IAthHost> Current;
    FAthDispatcher Dispatcher(Session, [&]() { return Current; });
    TSharedPtr<FJsonObject> Reply;
    TestTrue(TEXT("Ping envelope parses"), FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.ping"))), Reply));
    if (!Reply) return false;
    const FString Id = Reply->GetStringField(TEXT("correlationId"));
    TestEqual(TEXT("Ping success"), Reply->GetStringField(TEXT("status")), FString(TEXT("success")));
    TestFalse(TEXT("Correlation present"), Id.IsEmpty());
    const auto& Lines = Reply->GetArrayField(TEXT("lines"));
    TestEqual(TEXT("Command and terminal emitted"), Lines.Num(), 2);
    for (const auto& Line : Lines) TestTrue(TEXT("Sentinels carry envelope correlation"), Line->AsString().Contains(TEXT("id=") + Id));
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.execute forbidden e30="))), Reply);
    TestEqual(TEXT("Host absent error is structured"), Reply->GetStringField(TEXT("status")), FString(TEXT("failed")));
    TestTrue(TEXT("Host absent error reason retained"), Reply->GetArrayField(TEXT("lines")).Last()->AsString().Contains(TEXT("not_ready")));
    UWorld* World = UWorld::CreateWorld(EWorldType::Game, false, FName(TEXT("AthDispatcherTest")));
    if (!TestNotNull(TEXT("Native host world created"), World)) return false;
    Current = MakeShared<FAthWorldHost>(World, INDEX_NONE, Session);
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.state unknown.key"))), Reply);
    TestTrue(TEXT("Unknown state fails explicitly"), Reply->GetArrayField(TEXT("lines")).Last()->AsString().Contains(TEXT("unknown_key")));
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.execute ath.fixture.increment e30="))), Reply);
    TestEqual(TEXT("Registered mutation dispatched on game thread"), Reply->GetStringField(TEXT("status")), FString(TEXT("success")));
    FString Value;
    Current->TryGetState(TEXT("ath.fixture.counter"), Value);
    TestEqual(TEXT("Command changed fixture state"), Value, FString(TEXT("1")));
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.execute arbitrary.code e30="))), Reply);
    TestTrue(TEXT("Unregistered command forbidden"), Reply->GetArrayField(TEXT("lines")).Last()->AsString().Contains(TEXT("unknown_command")));
    Current.Reset();
    Session.Reset(true);
    FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Dispatcher.Execute(TEXT("harness.state game_ready"))), Reply);
    TestTrue(TEXT("Teardown readiness remains pollable"), Reply->GetArrayField(TEXT("lines")).Last()->AsString().Contains(TEXT("status=not_ready")));
    World->DestroyWorld(false);
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthTransportLifecycleTest, "ATH.Runtime.TransportLifecycleBounds",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthTransportLifecycleTest::RunTest(const FString& Parameters)
{
    FAthTransport Transport;
    TestFalse(TEXT("Inactive by default"), Transport.IsActive());
    TestFalse(TEXT("Invalid lower port rejected"), Transport.Start(-1, [](const FString&) { return FString(); }));
    TestFalse(TEXT("Invalid upper port rejected"), Transport.Start(65536, [](const FString&) { return FString(); }));
    Transport.Stop();
    Transport.Stop();
    Transport.Tick();
    TestFalse(TEXT("Idempotent shutdown stays inactive"), Transport.IsActive());
    return true;
}
#endif
