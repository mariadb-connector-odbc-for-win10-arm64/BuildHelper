using CommandLine;
using LibBuildHelper;
using NLog;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BuildHelper
{
    internal class Program
    {
        [Verb("cmake-vcxproj-setup-arm64x")]
        private class CMakeVCXProjSetupArm64xOptions
        {
            [Value(0, MetaName = "VcxProjPath", Required = true, HelpText = "Path to the VCXProj file to be fixed.")]
            public string VcxProjPath { get; set; } = "";
        }

        [Verb("sln-clone-cp")]
        private class SlnCloneCpOptions
        {
            [Value(0, MetaName = "SlnPath", Required = true, HelpText = "Path to the solution file to copy platform configurations from.")]
            public string SlnPath { get; set; } = "";

            [Value(1, MetaName = "ProjectNames", Required = true, HelpText = "Comma-separated list of project names to copy platform configurations to.")]
            public string ProjectNames { get; set; } = "";

            [Value(2, MetaName = "CopyFrom", Required = true, HelpText = "Platform to copy configurations from (e.g., ARM64).")]
            public string CopyFrom { get; set; } = "";

            [Value(3, MetaName = "CopyTo", Required = true, HelpText = "Platform to copy configurations to (e.g., ARM64EC).")]
            public string CopyTo { get; set; } = "";

            [Option('u', "update-project-file", Required = false, Default = true, HelpText = "Update the project file after copying configurations.")]
            public bool UpdateProjectFile { get; set; }
        }

        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<CMakeVCXProjSetupArm64xOptions, SlnCloneCpOptions>(args)
                .MapResult<CMakeVCXProjSetupArm64xOptions, SlnCloneCpOptions, int>(
                    DoCMakeVCXProjSetupArm64x,
                    DoSlnCloneCp,
                    ex => 1
                );
        }

        private static int DoSlnCloneCp(SlnCloneCpOptions o)
        {
            var slnCloneCp = new SlnCloneCp(
                cmakeVCXProjSetupArm64x: new CMakeVCXProjSetupArm64x()
            );

            slnCloneCp.Run(
                slnPath: Path.GetFullPath(o.SlnPath),
                projectNames: o.ProjectNames,
                copyFrom: o.CopyFrom,
                copyTo: o.CopyTo,
                updateProjectFile: o.UpdateProjectFile
            );

            return 0;
        }

        private static int DoCMakeVCXProjSetupArm64x(CMakeVCXProjSetupArm64xOptions o)
        {
            var cmakeVCXProjSetupArm64x = new CMakeVCXProjSetupArm64x();

            cmakeVCXProjSetupArm64x.Run(
                vcxProjPath: o.VcxProjPath
            );

            return 0;
        }
    }
}
