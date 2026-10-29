#include "AthHostSubsystem.h"
#include "Engine/GameInstance.h"
#include "Misc/AutomationTest.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthBlueprintSessionTest, "ATH.Runtime.BlueprintSessionIsolation",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthBlueprintSessionTest::RunTest(const FString& Parameters)
{
    FAthRuntimeModule* Module = FAthRuntimeModule::Instance;
    if (!TestNotNull(TEXT("Runtime module is live"), Module)) return false;
    UGameInstance* GameInstance = NewObject<UGameInstance>();
    UAthHostSubsystem* Host = NewObject<UAthHostSubsystem>(GameInstance);
    if (!TestNotNull(TEXT("Blueprint host subsystem created"), Host)) return false;

    for (const FName Name : {FName(TEXT("GetSessionGeneration")), FName(TEXT("ObserveEventForSession")), FName(TEXT("CompleteAsyncForSession"))})
    {
        const UFunction* Function = Host->FindFunction(Name);
        if (!TestNotNull(*FString::Printf(TEXT("Blueprint method %s is reflected"), *Name.ToString()), Function)) return false;
        TestTrue(*FString::Printf(TEXT("Method %s is Blueprint callable"), *Name.ToString()), Function->HasAnyFunctionFlags(FUNC_BlueprintCallable));
    }

    Module->Session.Reset(true);
    if (!FAthRuntime::IsRemoteActive())
    {
        TestEqual(TEXT("Disabled instrumentation has no Blueprint generation"), Host->GetSessionGeneration(), static_cast<int64>(0));
        Host->ObserveEventForSession(0, TEXT("disabled-event"));
        Host->CompleteAsyncForSession(0, TEXT("disabled"), TEXT("disabled-id"), true, TEXT(""));
        FString Status, Value;
        TestTrue(TEXT("Disabled event query exists"), Module->Session.TryGetState(TEXT("event:disabled-event"), Status, Value));
        TestEqual(TEXT("Disabled event was ignored"), Value, FString(TEXT("false")));
        TestTrue(TEXT("Disabled completion query exists"), Module->Session.TryGetState(TEXT("async:disabled-id"), Status, Value));
        TestEqual(TEXT("Disabled completion was ignored"), Status, FString(TEXT("not_ready")));
        return true;
    }

    const int64 OldGeneration = Host->GetSessionGeneration();
    TestTrue(TEXT("Active session has a positive generation"), OldGeneration > 0);
    Host->ObserveEventForSession(OldGeneration, TEXT("before-reset"));
    Host->CompleteAsyncForSession(OldGeneration, TEXT("before"), TEXT("old-id"), true, TEXT(""));
    FString Status, Value;
    TestTrue(TEXT("Initial event query exists"), Module->Session.TryGetState(TEXT("event:before-reset"), Status, Value));
    TestEqual(TEXT("Initial event was observed"), Value, FString(TEXT("true")));
    TestTrue(TEXT("Initial completion query exists"), Module->Session.TryGetState(TEXT("async:old-id"), Status, Value));
    TestEqual(TEXT("Initial completion was observed"), Status, FString(TEXT("ok")));

    Module->Session.Reset(true);
    const int64 NewGeneration = Host->GetSessionGeneration();
    TestTrue(TEXT("Full reset advances generation"), NewGeneration > OldGeneration);
    Host->ObserveEventForSession(OldGeneration, TEXT("stale-event"));
    Host->CompleteAsyncForSession(OldGeneration, TEXT("stale"), TEXT("stale-id"), true, TEXT(""));
    TestTrue(TEXT("Stale event query exists"), Module->Session.TryGetState(TEXT("event:stale-event"), Status, Value));
    TestEqual(TEXT("Late event was ignored"), Value, FString(TEXT("false")));
    TestTrue(TEXT("Stale completion query exists"), Module->Session.TryGetState(TEXT("async:stale-id"), Status, Value));
    TestEqual(TEXT("Late completion was ignored"), Status, FString(TEXT("not_ready")));

    Host->ObserveEventForSession(NewGeneration, TEXT("current-event"));
    Host->CompleteAsyncForSession(NewGeneration, TEXT("current"), TEXT("current-id"), true, TEXT(""));
    TestTrue(TEXT("Current event query exists"), Module->Session.TryGetState(TEXT("event:current-event"), Status, Value));
    TestEqual(TEXT("Current event was observed"), Value, FString(TEXT("true")));
    TestTrue(TEXT("Current completion query exists"), Module->Session.TryGetState(TEXT("async:current-id"), Status, Value));
    TestEqual(TEXT("Current completion was observed"), Status, FString(TEXT("ok")));
    Module->Session.Reset(true);
    return true;
}
