#include "AthHostSubsystem.h"
#include "AthRuntime.h"

bool UAthHostSubsystem::SetState(const FString& Key, const FString& Value)
{
    check(IsInGameThread());
    if (Key.IsEmpty() || Key.Len() > 256 || Value.Len() > 16384 || (!State.Contains(Key) && State.Num() >= 256)) return false;
    State.Add(Key, Value);
    return true;
}

void UAthHostSubsystem::RemoveState(const FString& Key) { check(IsInGameThread()); State.Remove(Key); }
void UAthHostSubsystem::ObserveEvent(const FString& EventName) { FAthRuntime::ObserveEvent(EventName); }
void UAthHostSubsystem::CompleteAsync(const FString& Name, const FString& CorrelationId, bool bSuccess, const FString& Error)
{ FAthRuntime::CompleteAsync(Name, CorrelationId, bSuccess, Error); }

bool UAthHostSubsystem::RegisterCommand(const FString& Command, FAthCommandHandler Handler)
{
    check(IsInGameThread());
    if (Command.IsEmpty() || Command.Len() > 256 || !Handler.IsBound() || (!Commands.Contains(Command) && Commands.Num() >= 256)) return false;
    Commands.Add(Command, Handler);
    return true;
}

void UAthHostSubsystem::UnregisterCommand(const FString& Command) { check(IsInGameThread()); Commands.Remove(Command); }
bool UAthHostSubsystem::TryGetState(const FString& Key, FString& Value) const
{
    if (const FString* Found = State.Find(Key)) { Value = *Found; return true; }
    return false;
}
TArray<FString> UAthHostSubsystem::GetStateKeys() const { TArray<FString> Keys; State.GetKeys(Keys); return Keys; }
TArray<FString> UAthHostSubsystem::GetCommands() const { TArray<FString> Keys; Commands.GetKeys(Keys); return Keys; }
FAthCommandResult UAthHostSubsystem::Execute(const FString& Command, const FString& ArgumentsJson, const FString& CorrelationId)
{
    check(IsInGameThread());
    // Copy before invoking: host code may unregister itself while handling the command.
    if (const FAthCommandHandler* Found = Commands.Find(Command))
    {
        FAthCommandHandler Handler = *Found;
        if (Handler.IsBound()) return Handler.Execute(ArgumentsJson, CorrelationId);
    }
    return FAthCommandResult::Failure(TEXT("unknown_command"));
}
void UAthHostSubsystem::Deinitialize()
{
    Commands.Reset(); State.Reset();
    Super::Deinitialize();
}
