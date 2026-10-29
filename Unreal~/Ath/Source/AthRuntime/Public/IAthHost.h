#pragma once

#include "CoreMinimal.h"
#include "AthTypes.h"
#include "Dom/JsonObject.h"

/**
 * Optional native game adapter. All methods run on the game thread, including teardown.
 * State getters are cheap, side-effect-free and return invariant text. No player/item
 * model is required. ATH retains a shared owner until replacement or UnregisterHost.
 */
class ATHRUNTIME_API IAthHost
{
public:
    virtual ~IAthHost() = default;
    /** Cancel owned work and detach subscriptions; called before replacement, including retained shared owners. Idempotent. */
    virtual void Shutdown() {}
    virtual FString GetHostName() const = 0;
    virtual bool IsValid() const { return true; }
    virtual bool IsReady() const = 0;
    virtual TArray<FString> GetCapabilities() const { return {}; }
    virtual TArray<FString> GetStateKeys() const { return {}; }
    virtual TArray<FString> GetCommands() const { return {}; }
    virtual bool TryGetState(const FString& Key, FString& Value) const = 0;
    virtual FAthCommandResult Execute(const FString& Command, const TSharedRef<FJsonObject>& Arguments,
        const FString& CorrelationId) { return FAthCommandResult::Failure(TEXT("unknown_command")); }
    virtual FAthCommandResult CaptureScreenshot(const FString& Label)
    { return FAthCommandResult::Failure(TEXT("unsupported")); }
    virtual FAthCommandResult Reset(const FString& Scope)
    { return FAthCommandResult::Success(); }
};
