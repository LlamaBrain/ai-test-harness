#pragma once

#include "CoreMinimal.h"

class IAthHost;

/** Game-thread host registration and bounded event/async observation surface. */
class ATHRUNTIME_API FAthRuntime
{
public:
    static void RegisterHost(const TSharedRef<IAthHost>& Host);
    static void UnregisterHost();
    static void ObserveEvent(const FString& EventName);
    static void CompleteAsync(const FString& Name, const FString& CorrelationId,
        bool bSuccess, const FString& Error = TEXT(""));
    static bool IsRemoteActive();
    /** Capture when starting delayed host work; guarded completion ignores callbacks from earlier sessions. */
    static uint64 GetSessionGeneration();
    static void ObserveEventForSession(uint64 Generation, const FString& EventName);
    static void CompleteAsyncForSession(uint64 Generation, const FString& Name, const FString& CorrelationId,
        bool bSuccess, const FString& Error = TEXT(""));
};
