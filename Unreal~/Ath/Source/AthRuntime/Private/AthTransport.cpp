#include "AthTransport.h"
#include "AthProtocol.h"
#include "IPAddress.h"
#include "SocketSubsystem.h"
#include "Sockets.h"

FAthTransport::~FAthTransport() { Stop(); }

void FAthTransport::Close(FSocket*& Socket)
{
    if (!Socket) return;
    Socket->Close();
    if (ISocketSubsystem* Sockets = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)) Sockets->DestroySocket(Socket);
    Socket = nullptr;
}

bool FAthTransport::Start(int32 Port, TFunction<FString(const FString&)> InDispatch)
{
    check(IsInGameThread());
    Stop();
    if (Port < 1 || Port > 65535) return false;
    ISocketSubsystem* Sockets = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
    if (!Sockets) return false;
    Listener = Sockets->CreateSocket(NAME_Stream, TEXT("ATH loopback"), false);
    if (!Listener) return false;
    TSharedRef<FInternetAddr> Address = Sockets->CreateInternetAddr();
    bool bValid = false;
    Address->SetIp(TEXT("127.0.0.1"), bValid);
    Address->SetPort(Port);
    if (!bValid || !Listener->SetNonBlocking(true) || !Listener->Bind(*Address) || !Listener->Listen(8))
    {
        Stop();
        return false;
    }
    Dispatch = MoveTemp(InDispatch);
    return true;
}

void FAthTransport::Stop()
{
    check(IsInGameThread());
    DisconnectClients();
    Close(Listener);
    Dispatch = nullptr;
}

void FAthTransport::DisconnectClients()
{
    check(IsInGameThread());
    // A registered command can trigger world cleanup synchronously during Dispatch.
    // Defer destruction until Tick releases its connection references.
    if (bTicking) { bDisconnectRequested = true; return; }
    for (FConnection& Connection : Connections) Close(Connection.Socket);
    Connections.Reset();
    bDisconnectRequested = false;
}

void FAthTransport::Reply(FConnection& Connection, const FString& Wire)
{
    FTCHARToUTF8 Encoded(*Wire);
    Connection.Output.Append(reinterpret_cast<const uint8*>(Encoded.Get()), Encoded.Length());
}

void FAthTransport::Tick()
{
    check(IsInGameThread());
    if (!Listener) return;
    bTicking = true;
    // Bound work per engine frame, including rejection of excess connections.
    for (int32 Accepted = 0; Accepted < 4; ++Accepted)
    {
        bool bPending = false;
        if (!Listener->HasPendingConnection(bPending) || !bPending) break;
        FSocket* Socket = Listener->Accept(TEXT("ATH request"));
        if (!Socket) break;
        if (Connections.Num() >= 8 || !Socket->SetNonBlocking(true)) { Close(Socket); continue; }
        Socket->SetNoDelay(true);
        FConnection Connection;
        Connection.Socket = Socket;
        Connection.Deadline = FPlatformTime::Seconds() + 5.0;
        Connections.Add(MoveTemp(Connection));
    }
    for (int32 Index = Connections.Num() - 1; Index >= 0; --Index)
    {
        FConnection& Connection = Connections[Index];
        bool bClose = bDisconnectRequested || FPlatformTime::Seconds() >= Connection.Deadline;
        if (!bClose && Connection.Output.IsEmpty())
        {
            uint32 Pending = 0;
            if (Connection.Socket->HasPendingData(Pending) && Pending > 0)
            {
                uint8 Buffer[8193];
                int32 Read = 0;
                const int32 Available = FMath::Min<int32>(Pending, sizeof(Buffer));
                if (!Connection.Socket->Recv(Buffer, Available, Read) || Read <= 0) bClose = true;
                else
                {
                    Connection.Input.Append(Buffer, Read);
                    const int32 Newline = Connection.Input.Find(static_cast<uint8>('\n'));
                    if (Connection.Input.Num() > FAthProtocol::MaxRequestBytes + 1)
                    {
                        Reply(Connection, FAthProtocol::Response(TEXT(""), TEXT("failed"), TEXT("request_too_large"), {}, 0));
                    }
                    else if (Newline != INDEX_NONE)
                    {
                        int32 Length = Newline;
                        if (Length > 0 && Connection.Input[Length - 1] == '\r') --Length;
                        FUTF8ToTCHAR Decoded(reinterpret_cast<const ANSICHAR*>(Connection.Input.GetData()), Length);
                        const FString Request(Decoded.Length(), Decoded.Get());
                        // One request per connection. No shell/console passthrough.
                        if (Newline + 1 != Connection.Input.Num())
                            Reply(Connection, FAthProtocol::Response(TEXT(""), TEXT("failed"), TEXT("bad_request"), {}, 0));
                        else if (FPlatformTime::Seconds() < Connection.Deadline)
                        {
                            const FString Response = Dispatch(Request);
                            if (!bDisconnectRequested) Reply(Connection, Response);
                            else bClose = true;
                        }
                    }
                }
            }
            else if (Connection.Socket->GetConnectionState() == SCS_ConnectionError) bClose = true;
        }
        if (!bClose && !Connection.Output.IsEmpty())
        {
            int32 Sent = 0;
            if (Connection.Socket->Send(Connection.Output.GetData() + Connection.Sent,
                Connection.Output.Num() - Connection.Sent, Sent))
            {
                Connection.Sent += Sent;
                bClose = Connection.Sent == Connection.Output.Num();
            }
            else if (Connection.Socket->GetConnectionState() == SCS_ConnectionError) bClose = true;
        }
        if (bClose)
        {
            Close(Connection.Socket);
            Connections.RemoveAtSwap(Index);
        }
    }
    bTicking = false;
    if (bDisconnectRequested) DisconnectClients();
}
