#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "AthProtocol.h"
#include "AthSession.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthProtocolEscapeTest, "ATH.Protocol.EscapingAndEnvelope",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthProtocolEscapeTest::RunTest(const FString& Parameters)
{
    TestEqual(TEXT("Sentinel escaping matches AthLog"), FAthProtocol::Escape(TEXT("a\\b\"c\r\nd")), FString(TEXT("a\\\\b\\\"c\\nd")));
    const FString Wire = FAthProtocol::Response(TEXT("abc"), TEXT("success"), TEXT(""),
        {TEXT("CMD:harness.state id=abc"), TEXT("OK:harness.state id=abc status=ok value=\"こんにちは\"")}, 3.0);
    TSharedPtr<FJsonObject> Object;
    TestTrue(TEXT("One JSON response parses"), FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Wire), Object));
    if (Object)
    {
        TestEqual(TEXT("Correlation preserved"), Object->GetStringField(TEXT("correlationId")), FString(TEXT("abc")));
        TestEqual(TEXT("Success preserved"), Object->GetStringField(TEXT("status")), FString(TEXT("success")));
        TestEqual(TEXT("Sentinel count preserved"), Object->GetArrayField(TEXT("lines")).Num(), 2);
    }
    TestTrue(TEXT("Wire is newline terminated"), Wire.EndsWith(TEXT("\n")));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthProtocolArgumentsTest, "ATH.Protocol.ArgumentsAndBounds",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthProtocolArgumentsTest::RunTest(const FString& Parameters)
{
    TArray<FString> Tokens;
    TestTrue(TEXT("Quoted console argument"), FAthProtocol::Tokenize(TEXT("test.echo \"two words\""), Tokens));
    TestEqual(TEXT("Two tokens"), Tokens.Num(), 2);
    if (Tokens.Num() == 2) TestEqual(TEXT("Quote removed"), Tokens[1], FString(TEXT("two words")));
    TestFalse(TEXT("Reject unterminated quote"), FAthProtocol::Tokenize(TEXT("test.echo \"broken"), Tokens));
    TestFalse(TEXT("Reject controls"), FAthProtocol::Tokenize(TEXT("harness.ping\nquit"), Tokens));
    TestFalse(TEXT("Reject empty request"), FAthProtocol::Tokenize(TEXT("  "), Tokens));
    TestFalse(TEXT("Reject oversized request"), FAthProtocol::Tokenize(FString::ChrN(8193, TEXT('a')), Tokens));
    TestEqual(TEXT("Label cannot traverse"), FAthProtocol::SanitizeLabel(TEXT("../../hello world")), FString(TEXT("______hello_world")));
    TSharedPtr<FJsonObject> Arguments;
    TestTrue(TEXT("Decode JSON object"), FAthProtocol::DecodeArguments(TEXT("eyJ4IjoxfQ=="), Arguments));
    TestFalse(TEXT("Reject JSON array"), FAthProtocol::DecodeArguments(TEXT("WzEsMl0="), Arguments));
    TestFalse(TEXT("Reject malformed base64"), FAthProtocol::DecodeArguments(TEXT("%%%"), Arguments));
    return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FAthSessionEventsTest, "ATH.Session.StickyEventsAndAsync",
    (EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::ServerContext | EAutomationTestFlags::CommandletContext) | EAutomationTestFlags::EngineFilter)
bool FAthSessionEventsTest::RunTest(const FString& Parameters)
{
    FAthSession Session;
    FString Status, Value;
    Session.TryGetState(TEXT("event:host.finished"), Status, Value);
    TestEqual(TEXT("Absent edge is false"), Value, FString(TEXT("false")));
    Session.ObserveEvent(TEXT("host.finished"));
    Session.TryGetState(TEXT("event:host.finished"), Status, Value);
    TestEqual(TEXT("Event latches"), Value, FString(TEXT("true")));
    const uint64 OriginalGeneration = Session.GetGeneration();
    Session.Reset(false);
    TestEqual(TEXT("Edge reset preserves operation generation"), Session.GetGeneration(), OriginalGeneration);
    Session.TryGetState(TEXT("event:host.finished"), Status, Value);
    TestEqual(TEXT("Reset clears edges"), Value, FString(TEXT("false")));
    Session.TryGetState(TEXT("async:missing"), Status, Value);
    TestEqual(TEXT("Unknown async remains pollable"), Status, FString(TEXT("not_ready")));
    Session.CompleteAsync(TEXT("load"), TEXT("one"), false, TEXT("failed"));
    Session.TryGetState(TEXT("async:one"), Status, Value);
    TestEqual(TEXT("Failure still completes"), Value, FString(TEXT("done")));
    Session.TryGetState(TEXT("async_success:one"), Status, Value);
    TestEqual(TEXT("Failure never claims success"), Value, FString(TEXT("false")));
    Session.Reset(false);
    Session.TryGetState(TEXT("async:one"), Status, Value);
    TestEqual(TEXT("Edge reset retains async"), Value, FString(TEXT("done")));
    for (int32 I = 0; I < 17; ++I) Session.CompleteAsync(TEXT("bounded"), FString::FromInt(I), true, TEXT(""));
    Session.TryGetState(TEXT("async:one"), Status, Value);
    TestEqual(TEXT("Old async evicted"), Status, FString(TEXT("not_ready")));
    Session.Reset(true);
    TestTrue(TEXT("Full reset fences callbacks from prior session"), Session.GetGeneration() > OriginalGeneration);
    Session.TryGetState(TEXT("async:16"), Status, Value);
    TestEqual(TEXT("Full reset clears async"), Status, FString(TEXT("not_ready")));
    return true;
}
#endif
