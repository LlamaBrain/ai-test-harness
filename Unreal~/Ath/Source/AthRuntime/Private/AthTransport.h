#pragma once

#include "CoreMinimal.h"

class FSocket;

/** Nonblocking, bounded loopback transport. Tick and callbacks belong to the game thread. */
class FAthTransport
{
public:
    ~FAthTransport();
    bool Start(int32 Port, TFunction<FString(const FString&)> InDispatch);
    void Stop();
    void DisconnectClients();
    void Tick();
    bool IsActive() const { return Listener != nullptr; }

private:
    struct FConnection
    {
        FSocket* Socket = nullptr;
        TArray<uint8> Input;
        TArray<uint8> Output;
        int32 Sent = 0;
        double Deadline = 0;
    };
    FSocket* Listener = nullptr;
    TArray<FConnection> Connections;
    TFunction<FString(const FString&)> Dispatch;
    bool bTicking = false;
    bool bDisconnectRequested = false;
    void Close(FSocket*& Socket);
    void Reply(FConnection& Connection, const FString& Wire);
};
