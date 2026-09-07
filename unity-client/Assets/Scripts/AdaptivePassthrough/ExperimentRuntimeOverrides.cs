public enum ExperimentPresentationOverride
{
    UseUserSettings = 0,
    SuppressAll = 1,
    StaticOnly = 2,
    StaticAndDynamic = 3
}

public enum BoundaryVisibilityOverride
{
    UseConfiguredPolicy = 0,
    ForceVisible = 1,
    ForceSuppressed = 2
}

public static class ExperimentRuntimeOverrideResolver
{
    public static bool TryResolve(
        int conditionIndex,
        out ExperimentPresentationOverride presentation,
        out BoundaryVisibilityOverride boundary)
    {
        switch (conditionIndex)
        {
            case 0:
                presentation = ExperimentPresentationOverride.SuppressAll;
                boundary = BoundaryVisibilityOverride.ForceVisible;
                return true;
            case 1:
                presentation = ExperimentPresentationOverride.StaticOnly;
                boundary = BoundaryVisibilityOverride.ForceSuppressed;
                return true;
            case 2:
                presentation =
                    ExperimentPresentationOverride.StaticAndDynamic;
                boundary = BoundaryVisibilityOverride.ForceSuppressed;
                return true;
            default:
                presentation = ExperimentPresentationOverride.UseUserSettings;
                boundary = BoundaryVisibilityOverride.UseConfiguredPolicy;
                return false;
        }
    }
}

public static class ExperimentScoreRules
{
    public static int ApplyShotHit(int currentScore, bool optionalTarget)
    {
        return optionalTarget ? currentScore + 100 : currentScore / 2;
    }

    public static int ApplyBodyHit(int currentScore, bool optionalTarget)
    {
        return optionalTarget ? currentScore - 100 : 0;
    }
}
