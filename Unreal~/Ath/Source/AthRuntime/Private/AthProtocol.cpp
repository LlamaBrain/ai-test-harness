#include "AthProtocol.h"

#include "Dom/JsonObject.h"
#include "Misc/Base64.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

FString FAthProtocol::Escape(const FString& Value)
{
    return Value.Replace(TEXT("\\"), TEXT("\\\\")).Replace(TEXT("\""), TEXT("\\\""))
        .Replace(TEXT("\r"), TEXT("")).Replace(TEXT("\n"), TEXT("\\n"));
}

FString FAthProtocol::Response(const FString& CorrelationId, const FString& Status,
    const FString& FailReason, const TArray<FString>& Lines, double ElapsedMs, bool bTruncated)
{
    const TSharedRef<FJsonObject> Object = MakeShared<FJsonObject>();
    Object->SetStringField(TEXT("correlationId"), CorrelationId);
    Object->SetStringField(TEXT("status"), Status);
    Object->SetStringField(TEXT("failReason"), FailReason);
    TArray<TSharedPtr<FJsonValue>> JsonLines;
    for (const FString& Line : Lines) JsonLines.Add(MakeShared<FJsonValueString>(Line));
    Object->SetArrayField(TEXT("lines"), JsonLines);
    Object->SetNumberField(TEXT("elapsedMs"), ElapsedMs);
    Object->SetBoolField(TEXT("truncated"), bTruncated);
    FString Result;
    FJsonSerializer::Serialize(Object, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Result));
    if (FTCHARToUTF8(*Result).Length() > MaxResponseBytes)
    {
        return Response(CorrelationId, TEXT("failed"), TEXT("response_too_large"), {}, ElapsedMs, true);
    }
    return Result + TEXT("\n");
}

bool FAthProtocol::Tokenize(const FString& Command, TArray<FString>& Tokens)
{
    Tokens.Reset();
    if (Command.IsEmpty() || Command.Len() > MaxRequestBytes || FTCHARToUTF8(*Command).Length() > MaxRequestBytes) return false;
    FString Token;
    bool bQuoted = false;
    bool bStarted = false;
    for (int32 I = 0; I < Command.Len(); ++I)
    {
        const TCHAR C = Command[I];
        if (C < TEXT(' ') || C == 127) return false;
        if (C == TEXT('"')) { bQuoted = !bQuoted; bStarted = true; continue; }
        if (C == TEXT('\\') && bQuoted && I + 1 < Command.Len() &&
            (Command[I + 1] == TEXT('"') || Command[I + 1] == TEXT('\\')))
        {
            Token.AppendChar(Command[++I]); bStarted = true; continue;
        }
        if (C == TEXT(' ') && !bQuoted)
        {
            if (bStarted) { Tokens.Add(MoveTemp(Token)); Token.Reset(); bStarted = false; }
        }
        else { Token.AppendChar(C); bStarted = true; }
    }
    if (bQuoted) return false;
    if (bStarted) Tokens.Add(MoveTemp(Token));
    return Tokens.Num() > 0;
}

bool FAthProtocol::DecodeArguments(const FString& Encoded, TSharedPtr<FJsonObject>& Arguments)
{
    Arguments.Reset();
    if (Encoded.Len() > MaxRequestBytes) return false;
    FString Json;
    return FBase64::Decode(Encoded, Json) && Json.Len() <= MaxRequestBytes &&
        FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Arguments) && Arguments.IsValid();
}

FString FAthProtocol::SanitizeLabel(const FString& Label)
{
    FString Result = Label.Left(80);
    if (Result.IsEmpty()) return TEXT("snap");
    for (TCHAR& C : Result)
    {
        if (!((C >= TEXT('a') && C <= TEXT('z')) || (C >= TEXT('A') && C <= TEXT('Z')) ||
            (C >= TEXT('0') && C <= TEXT('9')) || C == TEXT('-') || C == TEXT('_'))) C = TEXT('_');
    }
    return Result;
}
