#pragma once

#include "CoreMinimal.h"
#include "AthSession.h"

class IAthHost;

/** Synchronous command router. Game-thread execution, asynchronous work returns dispatched. */
class FAthDispatcher
{
public:
    FAthDispatcher(FAthSession& InSession, TFunction<TSharedPtr<IAthHost>()> InHostProvider)
        : Session(InSession), HostProvider(MoveTemp(InHostProvider)) {}
    FString Execute(const FString& Request);

private:
    FAthSession& Session;
    TFunction<TSharedPtr<IAthHost>()> HostProvider;
};
