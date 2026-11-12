#include "AthSession.h"

void FAthSession::ObserveEvent(const FString& EventName)
{
    check(IsInGameThread());
    if (!EventName.IsEmpty() && EventName.Len() <= 256 && (Events.Contains(EventName) || Events.Num() < 256)) Events.Add(EventName);
}

void FAthSession::CompleteAsync(const FString& Name, const FString& Id, bool bSuccess, const FString& Error)
{
    check(IsInGameThread());
    if (Id.IsEmpty() || Id.Len() > 256) return;
    Async.RemoveAll([&](const FAsyncRecord& Record) { return Record.Id == Id; });
    Async.Add({Name.Left(256), Id, bSuccess, Error.Left(4096)});
    while (Async.Num() > 16) Async.RemoveAt(0);
}

bool FAthSession::TryGetState(const FString& Key, FString& Status, FString& Value) const
{
    Status = TEXT("ok");
    Value.Reset();
    if (Key.StartsWith(TEXT("event:")) && Key.Len() > 6)
    {
        Value = Events.Contains(Key.Mid(6)) ? TEXT("true") : TEXT("false");
        return true;
    }
    if (Key == TEXT("last_async"))
    {
        if (Async.IsEmpty()) Value = TEXT("empty");
        else
        {
            const FAsyncRecord& Record = Async.Last();
            Value = FString::Printf(TEXT("name=%s;id=%s;success=%s;error=%s"), *Record.Name, *Record.Id,
                Record.bSuccess ? TEXT("true") : TEXT("false"), *Record.Error);
        }
        return true;
    }
    const bool bSuccessQuery = Key.StartsWith(TEXT("async_success:"));
    if (bSuccessQuery || Key.StartsWith(TEXT("async:")))
    {
        const FString Id = Key.Mid(bSuccessQuery ? 14 : 6);
        for (const FAsyncRecord& Record : Async)
        {
            if (Record.Id != Id) continue;
            Value = bSuccessQuery ? (Record.bSuccess ? TEXT("true") : TEXT("false")) : TEXT("done");
            return true;
        }
        Status = TEXT("not_ready"); Value = TEXT("missing"); return true;
    }
    return false;
}

void FAthSession::Reset(bool bAll)
{
    check(IsInGameThread());
    Events.Reset();
    if (bAll)
    {
        Async.Reset();
        // A replacement world or host must reject callbacks from its predecessor.
        ++Generation;
    }
}
