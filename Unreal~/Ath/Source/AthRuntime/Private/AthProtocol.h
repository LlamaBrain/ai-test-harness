#pragma once

#include "CoreMinimal.h"

class FJsonObject;

/** ATH v1 wire formatting; same sentinel grammar as Unity AthLog/protocol.js. */
struct FAthProtocol
{
    static constexpr int32 MaxRequestBytes = 8192;
    static constexpr int32 MaxResponseBytes = 65536;
    static FString Escape(const FString& Value);
    static FString Response(const FString& CorrelationId, const FString& Status,
        const FString& FailReason, const TArray<FString>& Lines, double ElapsedMs, bool bTruncated = false);
    static bool Tokenize(const FString& Command, TArray<FString>& Tokens);
    static bool DecodeArguments(const FString& Encoded, TSharedPtr<FJsonObject>& Arguments);
    static FString SanitizeLabel(const FString& Label);
};
