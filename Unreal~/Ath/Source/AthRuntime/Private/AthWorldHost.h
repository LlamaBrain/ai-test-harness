#pragma once

#include "IAthHost.h"
#include "AthSession.h"

class UWorld;
class UAthHostSubsystem;

/** Built-in game-independent fixture and read-only native world observation. */
class FAthWorldHost final : public IAthHost
{
public:
    FAthWorldHost(UWorld* InWorld, int32 InPieInstance, FAthSession& InSession);
    virtual ~FAthWorldHost() override;
    virtual void Shutdown() override;
    virtual FString GetHostName() const override;
    virtual bool IsValid() const override;
    virtual bool IsReady() const override;
    virtual TArray<FString> GetCapabilities() const override;
    virtual TArray<FString> GetStateKeys() const override;
    virtual TArray<FString> GetCommands() const override;
    virtual bool TryGetState(const FString& Key, FString& Value) const override;
    virtual FAthCommandResult Execute(const FString& Command, const TSharedRef<FJsonObject>& Arguments,
        const FString& CorrelationId) override;
    virtual FAthCommandResult CaptureScreenshot(const FString& Label) override;
    virtual FAthCommandResult Reset(const FString& Scope) override;
    void Tick();
    UWorld* GetWorld() const { return World.Get(); }

private:
    TWeakObjectPtr<UWorld> World;
    int32 PieInstance;
    FAthSession& Session;
    int32 Counter = 0;
    bool bActive = true;
    FString LastAction = TEXT("ready");
    struct FPending { FString Id; double Due; };
    TArray<FPending> Pending;
    FString ScreenshotPath;
    double ScreenshotDeadline = 0;
    UAthHostSubsystem* GetSubsystem() const;
};
