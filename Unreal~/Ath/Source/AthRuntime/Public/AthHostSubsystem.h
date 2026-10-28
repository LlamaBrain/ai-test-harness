#pragma once

#include "CoreMinimal.h"
#include "Subsystems/GameInstanceSubsystem.h"
#include "AthTypes.h"
#include "AthHostSubsystem.generated.h"

/** Minimal Blueprint/C++ host extension. No actor, pawn or gameplay plugin required. */
UCLASS()
class ATHRUNTIME_API UAthHostSubsystem : public UGameInstanceSubsystem
{
    GENERATED_BODY()

public:
    UFUNCTION(BlueprintCallable, Category = "ATH")
    bool SetState(const FString& Key, const FString& Value);

    UFUNCTION(BlueprintCallable, Category = "ATH")
    void RemoveState(const FString& Key);

    UFUNCTION(BlueprintCallable, Category = "ATH")
    void ObserveEvent(const FString& EventName);

    UFUNCTION(BlueprintCallable, Category = "ATH")
    void CompleteAsync(const FString& Name, const FString& CorrelationId, bool bSuccess, const FString& Error);

    UFUNCTION(BlueprintCallable, Category = "ATH")
    bool RegisterCommand(const FString& Command, FAthCommandHandler Handler);

    UFUNCTION(BlueprintCallable, Category = "ATH")
    void UnregisterCommand(const FString& Command);

    bool TryGetState(const FString& Key, FString& Value) const;
    TArray<FString> GetStateKeys() const;
    TArray<FString> GetCommands() const;
    FAthCommandResult Execute(const FString& Command, const FString& ArgumentsJson, const FString& CorrelationId);
    virtual void Deinitialize() override;

private:
    TMap<FString, FString> State;
    TMap<FString, FAthCommandHandler> Commands;
};
