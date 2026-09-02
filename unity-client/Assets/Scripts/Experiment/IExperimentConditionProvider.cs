namespace TeamVR.Experiment
{
    public interface IExperimentConditionProvider
    {
        ExperimentCondition CurrentCondition { get; }
        bool RoundActive { get; }
        long RoundSequence { get; }
        string RoundId { get; }
        ExperimentPresentationOverride PresentationOverride { get; }
        BoundaryVisibilityOverride BoundaryOverride { get; }
        bool RequestedBoundarySuppression { get; }
        bool ActualBoundarySuppressed { get; }
    }
}
