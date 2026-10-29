using UnrealBuildTool;

public class AthRuntime : ModuleRules
{
    public AthRuntime(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "Json" });
        PrivateDependencyModuleNames.AddRange(new[] { "Sockets", "Projects", "RHI" });
    }
}
