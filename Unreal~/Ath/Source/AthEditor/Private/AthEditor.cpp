#include "Modules/ModuleManager.h"
#include "AthRuntime.h"
#include "HAL/IConsoleManager.h"
#include "Tests/AutomationEditorCommon.h"

class FAthEditorModule final : public IModuleInterface
{
public:
    virtual void StartupModule() override
    {
        // Editor-local controls only. Runtime TCP still accepts registered harness commands exclusively.
        StartCommand = IConsoleManager::Get().RegisterConsoleCommand(TEXT("ath.editor.pie.start"), TEXT("Start an ATH validation PIE session"),
            FConsoleCommandDelegate::CreateLambda([]() { if (FAthRuntime::IsRemoteActive()) { FStartPIECommand Start(false); Start.Update(); } }), ECVF_Default);
        StopCommand = IConsoleManager::Get().RegisterConsoleCommand(TEXT("ath.editor.pie.stop"), TEXT("End the ATH validation PIE session"),
            FConsoleCommandDelegate::CreateLambda([]() { if (FAthRuntime::IsRemoteActive()) { FEndPlayMapCommand Stop; Stop.Update(); } }), ECVF_Default);
    }
    virtual void ShutdownModule() override
    {
        if (StartCommand) IConsoleManager::Get().UnregisterConsoleObject(StartCommand);
        if (StopCommand) IConsoleManager::Get().UnregisterConsoleObject(StopCommand);
    }
private:
    IConsoleObject* StartCommand = nullptr;
    IConsoleObject* StopCommand = nullptr;
};

IMPLEMENT_MODULE(FAthEditorModule, AthEditor)
