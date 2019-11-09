using JetBrains.Application;
using JetBrains.Application.Components;
using JetBrains.Collections.Viewable;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Host.Features;
using JetBrains.ReSharper.Host.Features.UnitTesting;
using JetBrains.ReSharper.Plugins.Unity.ProjectModel;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.Rider.Model;

namespace JetBrains.ReSharper.Plugins.Unity.Rider
{
    [ShellComponent]
    public class UnityRiderUnitTestCoverageAvailabilityChecker : IRiderUnitTestCoverageAvailabilityChecker, IHideImplementation<DefaultRiderUnitTestCoverageAvailabilityChecker>
    {
        // this method should be very fast as it gets called a lot
        public HostProviderAvailability GetAvailability(IUnitTestElement element)
        {
            var solution = element.Id.Project.GetSolution();
            var tracker = solution.GetComponent<UnitySolutionTracker>();
            if (tracker.IsUnityProject.HasValue() && !tracker.IsUnityProject.Value)
                return HostProviderAvailability.Available;

            var frontendBackendModel = solution.GetProtocolSolution().GetFrontendBackendModel();
            return frontendBackendModel.UnitTestPreference.Value == UnitTestLaunchPreference.NUnit
                ? HostProviderAvailability.Available
                : HostProviderAvailability.Nonexistent;
        }
    }
}