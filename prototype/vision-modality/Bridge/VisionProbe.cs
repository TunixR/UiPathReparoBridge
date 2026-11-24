// PROTOTYPE for ReparoHome#49: throwaway, never merged. Runs ProbeRunner inside the UiPath Robot Executor.
using System.Activities;
using System.Diagnostics;

namespace Reparo.UiPathBridge
{
    public sealed class ReparoVisionProbe : CodeActivity
    {
        public InArgument<string> Script { get; set; }
        public InArgument<string> OutDir { get; set; }

        protected override void Execute(CodeActivityContext context) =>
            ProbeRunner.Run(Script.Get(context), OutDir.Get(context), $"UiPath ({Process.GetCurrentProcess().ProcessName})");
    }
}
