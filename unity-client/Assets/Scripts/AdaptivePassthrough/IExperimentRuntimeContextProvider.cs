namespace TeamVR.AdaptivePassthrough
{
    // Assembly-safe logging view of the experiment runtime. The game layer
    // lives in Assembly-CSharp and implements this contract without making
    // the AdaptivePassthrough runtime assembly depend on gameplay scripts.
    public interface IExperimentRuntimeContextProvider
    {
        string ConditionName { get; }
        bool RoundActive { get; }
        long RoundSequence { get; }
        string RoundId { get; }
        string PresentationOverrideName { get; }
        string BoundaryOverrideName { get; }
        bool RequestedBoundarySuppression { get; }
        bool ActualBoundarySuppressed { get; }
    }
}
