using UnrealBuildTool;

public class AthEditor : ModuleRules
{
    public AthEditor(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PrivateDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "UnrealEd", "AthRuntime", "Sockets", "Json" });
    }
}
