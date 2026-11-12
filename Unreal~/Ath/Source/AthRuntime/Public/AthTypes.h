#pragma once

#include "CoreMinimal.h"
#include "AthTypes.generated.h"

/** Result of an ATH host command. Dispatched operations must later report completion. */
USTRUCT(BlueprintType)
struct ATHRUNTIME_API FAthCommandResult
{
    GENERATED_BODY()

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "ATH")
    bool bSuccess = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "ATH")
    bool bDispatched = false;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "ATH")
    FString Value;

    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "ATH")
    FString Error = TEXT("unsupported");

    static FAthCommandResult Success(const FString& InValue = TEXT(""))
    {
        FAthCommandResult Result;
        Result.bSuccess = true;
        Result.Value = InValue;
        Result.Error.Reset();
        return Result;
    }

    static FAthCommandResult Failure(const FString& InError)
    {
        FAthCommandResult Result;
        Result.Error = InError;
        return Result;
    }
};

DECLARE_DYNAMIC_DELEGATE_RetVal_TwoParams(FAthCommandResult, FAthCommandHandler,
    const FString&, ArgumentsJson, const FString&, CorrelationId);
