#include "AthDispatcher.h"

#include "AthProtocol.h"
#include "IAthHost.h"
#include "Misc/EngineVersion.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
    FString BuildMode()
    {
#if UE_BUILD_DEBUG
        return TEXT("Debug");
#elif UE_BUILD_DEVELOPMENT
        return TEXT("Development");
#else
        return TEXT("OtherInstrumented");
#endif
    }

    void SetStrings(const TSharedRef<FJsonObject>& Object, const FString& Key, const TArray<FString>& Values)
    {
        TArray<TSharedPtr<FJsonValue>> Array;
        for (const FString& Value : Values) Array.Add(MakeShared<FJsonValueString>(Value));
        Object->SetArrayField(Key, Array);
    }

    FString ToJson(const TSharedRef<FJsonObject>& Object)
    {
        FString Text;
        FJsonSerializer::Serialize(Object, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Text));
        return Text;
    }
}

FString FAthDispatcher::Execute(const FString& Request)
{
    check(IsInGameThread());
    const double Started = FPlatformTime::Seconds();
    const FString Id = FGuid::NewGuid().ToString(EGuidFormats::Digits).Left(8);
    TArray<FString> Tokens;
    if (!FAthProtocol::Tokenize(Request, Tokens)) return FAthProtocol::Response(Id, TEXT("failed"), TEXT("bad_request"), {}, 0);
    const FString Command = Tokens[0];
    TArray<FString> Lines = {FString::Printf(TEXT("CMD:%s id=%s"), *Command, *Id)};
    auto Respond = [&](const FString& Payload, const FString& Error = TEXT(""), bool bDispatched = false)
    {
        Lines.Add(FString::Printf(TEXT("%s:%s id=%s %s"), Error.IsEmpty() ? TEXT("OK") : TEXT("ERR"), *Command, *Id,
            Error.IsEmpty() ? *Payload : *FString::Printf(TEXT("reason=\"%s\""), *FAthProtocol::Escape(Error))));
        return FAthProtocol::Response(Id, !Error.IsEmpty() ? TEXT("failed") : bDispatched ? TEXT("dispatched") : TEXT("success"),
            Error.IsEmpty() ? TEXT("") : TEXT("command_error"), Lines, (FPlatformTime::Seconds() - Started) * 1000.0);
    };
    auto Result = [&](const FAthCommandResult& Value)
    {
        if (!Value.bSuccess) return Respond(TEXT(""), Value.Error.IsEmpty() ? TEXT("command_error") : Value.Error);
        return Respond(FString::Printf(TEXT("value=\"%s\"%s"), *FAthProtocol::Escape(Value.Value),
            Value.bDispatched ? TEXT(" [dispatched]") : TEXT("")), TEXT(""), Value.bDispatched);
    };
    const TSharedPtr<IAthHost> Host = HostProvider();
    const bool bHostValid = Host.IsValid() && Host->IsValid();

    if (Command == TEXT("harness.ping") && Tokens.Num() == 1) return Respond(TEXT("pong=true"));
    if (Command == TEXT("harness.version") && Tokens.Num() == 1)
        return Respond(FString::Printf(TEXT("version=0.3.1 protocolVersion=1 engine=unreal engineVersion=\"%s\" buildMode=%s"),
            *FAthProtocol::Escape(FEngineVersion::Current().ToString()), *BuildMode()));
    if (Command == TEXT("test.echo") && Tokens.Num() == 2)
        return Respond(FString::Printf(TEXT("message=\"%s\""), *FAthProtocol::Escape(Tokens[1])));
    if (Command == TEXT("harness.state") && Tokens.Num() == 2)
    {
        const FString& Key = Tokens[1];
        FString Status = TEXT("ok"), Value;
        if (Key == TEXT("engine_name")) Value = TEXT("unreal");
        else if (Key == TEXT("package_version")) Value = TEXT("0.3.1");
        else if (Key == TEXT("bridge_ready")) Value = TEXT("true");
        else if (Key == TEXT("adapter_ready")) Value = bHostValid ? TEXT("true") : TEXT("false");
        else if (Key == TEXT("host_name")) Value = bHostValid ? Host->GetHostName() : TEXT("<none>");
        else if (Key == TEXT("game_ready"))
        {
            if (!bHostValid) Status = TEXT("not_ready");
            else Value = Host->IsReady() ? TEXT("true") : TEXT("false");
        }
        else if (Session.TryGetState(Key, Status, Value)) {}
        else if (!bHostValid) Status = TEXT("not_ready");
        else if (!Host->TryGetState(Key, Value)) return Respond(TEXT(""), TEXT("unknown_key"));
        return Respond(FString::Printf(TEXT("key=\"%s\" status=%s value=\"%s\""),
            *FAthProtocol::Escape(Key), *Status, *FAthProtocol::Escape(Value)));
    }
    if (Command == TEXT("harness.capabilities") && Tokens.Num() == 1)
    {
        const TSharedRef<FJsonObject> Metadata = MakeShared<FJsonObject>();
        Metadata->SetNumberField(TEXT("protocolVersion"), 1);
        Metadata->SetStringField(TEXT("engine"), TEXT("unreal"));
        Metadata->SetStringField(TEXT("engineVersion"), FEngineVersion::Current().ToString());
        Metadata->SetStringField(TEXT("buildMode"), BuildMode());
        Metadata->SetStringField(TEXT("athVersion"), TEXT("0.3.1"));
        Metadata->SetStringField(TEXT("hostName"), bHostValid ? Host->GetHostName() : TEXT("<none>"));
        TArray<FString> Capabilities = {TEXT("state"), TEXT("commands"), TEXT("events"), TEXT("async"), TEXT("reset")};
        TArray<FString> StateKeys = {TEXT("engine_name"), TEXT("host_name"), TEXT("package_version"), TEXT("bridge_ready"),
            TEXT("adapter_ready"), TEXT("game_ready"), TEXT("event:<name>"), TEXT("async:<id>"), TEXT("async_success:<id>"), TEXT("last_async")};
        TArray<FString> Commands = {TEXT("harness.ping"), TEXT("harness.state"), TEXT("harness.capabilities"), TEXT("harness.version"),
            TEXT("harness.execute"), TEXT("harness.reset"), TEXT("harness.snap"), TEXT("test.echo")};
        if (bHostValid)
        {
            for (const FString& Value : Host->GetCapabilities()) Capabilities.AddUnique(Value);
            for (const FString& Value : Host->GetStateKeys()) StateKeys.AddUnique(Value);
            for (const FString& Value : Host->GetCommands()) Commands.AddUnique(Value);
        }
        Capabilities.Sort(); StateKeys.Sort(); Commands.Sort();
        SetStrings(Metadata, TEXT("capabilities"), Capabilities);
        SetStrings(Metadata, TEXT("stateKeys"), StateKeys);
        SetStrings(Metadata, TEXT("commands"), Commands);
        return Respond(FString::Printf(TEXT("value=\"%s\""), *FAthProtocol::Escape(ToJson(Metadata))));
    }
    if (Command == TEXT("harness.reset") && (Tokens.Num() == 1 || Tokens.Num() == 2))
    {
        const FString Scope = Tokens.Num() == 1 ? TEXT("edges") : Tokens[1];
        if (Scope != TEXT("edges") && Scope != TEXT("all")) return Respond(TEXT(""), TEXT("unsupported_scope"));
        if (bHostValid)
        {
            const FAthCommandResult ResetResult = Host->Reset(Scope);
            if (!ResetResult.bSuccess) return Result(ResetResult);
        }
        Session.Reset(Scope == TEXT("all"));
        return Respond(TEXT("reset=true"));
    }
    if (Command == TEXT("harness.execute") && Tokens.Num() == 3)
    {
        if (!bHostValid) return Respond(TEXT(""), TEXT("not_ready"));
        if (!Host->GetCommands().Contains(Tokens[1])) return Respond(TEXT(""), TEXT("unknown_command"));
        TSharedPtr<FJsonObject> Arguments;
        if (!FAthProtocol::DecodeArguments(Tokens[2], Arguments)) return Respond(TEXT(""), TEXT("bad_arguments"));
        return Result(Host->Execute(Tokens[1], Arguments.ToSharedRef(), Id));
    }
    if (Command == TEXT("harness.snap") && Tokens.Num() == 2)
    {
        if (!bHostValid) return Respond(TEXT(""), TEXT("not_ready"));
        const FAthCommandResult Capture = Host->CaptureScreenshot(FAthProtocol::SanitizeLabel(Tokens[1]));
        if (!Capture.bSuccess) return Result(Capture);
        return Respond(FString::Printf(TEXT("path=\"%s\" status=pending"), *FAthProtocol::Escape(Capture.Value)));
    }
    if (Command.StartsWith(TEXT("harness.")) || Command == TEXT("test.echo")) return Respond(TEXT(""), TEXT("bad_request"));
    // Direct host command convenience: explicit string argv, never shell evaluation.
    if (bHostValid)
    {
        if (!Host->GetCommands().Contains(Command)) return Respond(TEXT(""), TEXT("unknown_command"));
        const TSharedRef<FJsonObject> Arguments = MakeShared<FJsonObject>();
        TArray<TSharedPtr<FJsonValue>> Args;
        for (int32 I = 1; I < Tokens.Num(); ++I) Args.Add(MakeShared<FJsonValueString>(Tokens[I]));
        Arguments->SetArrayField(TEXT("args"), Args);
        return Result(Host->Execute(Command, Arguments, Id));
    }
    return Respond(TEXT(""), TEXT("unknown_command"));
}
