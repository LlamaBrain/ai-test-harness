#pragma once

#include "CoreMinimal.h"

/** Game-thread-owned bounded observation state, independent of any game model. */
class FAthSession
{
public:
    void ObserveEvent(const FString& EventName);
    void CompleteAsync(const FString& Name, const FString& Id, bool bSuccess, const FString& Error);
    bool TryGetState(const FString& Key, FString& Status, FString& Value) const;
    void Reset(bool bAll);
    uint64 GetGeneration() const { return Generation; }

private:
    struct FAsyncRecord { FString Name; FString Id; bool bSuccess; FString Error; };
    TSet<FString> Events;
    TArray<FAsyncRecord> Async;
    uint64 Generation = 1;
};
