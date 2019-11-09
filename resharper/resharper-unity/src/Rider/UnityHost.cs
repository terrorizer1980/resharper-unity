﻿using System;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Host.Features;
using JetBrains.Rider.Model;

namespace JetBrains.ReSharper.Plugins.Unity.Rider
{
    [SolutionComponent]
    public class UnityHost
    {
        private readonly bool myIsInTests;
        private readonly FrontendBackendModel myModel;

        public UnityHost(ISolution solution, bool isInTests = false)
        {
            myIsInTests = isInTests;
            if (myIsInTests)
                return;

            myModel = solution.GetProtocolSolution().GetFrontendBackendModel();
        }

        public void PerformModelAction(Action<FrontendBackendModel> action)
        {
            if (myIsInTests)
                return;

            action(myModel);
        }

        public T GetValue<T>(Func<FrontendBackendModel, T> getter)
        {
            return getter(myModel);
        }
    }
}