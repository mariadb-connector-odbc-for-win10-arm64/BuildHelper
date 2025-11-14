using NLog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibBuildHelper
{
    public class SlnCloneCp
    {
        private readonly ILogger _logger = LogManager.GetLogger("SlnCloneCp");
        private readonly CMakeVCXProjSetupArm64x _cmakeVCXProjSetupArm64x;

        public SlnCloneCp(
            CMakeVCXProjSetupArm64x cmakeVCXProjSetupArm64x)
        {
            _cmakeVCXProjSetupArm64x = cmakeVCXProjSetupArm64x;
        }

        public void Run(string slnPath, string projectNames, string copyFrom, string copyTo, bool updateProjectFile)
        {
            var anyChanged = false;

            var slnHelper = new SlnHelper();

            _logger.Info("ProcessingSlnFile: {0}", slnPath);

            var lines = slnHelper.LoadLinesFromFile(slnPath)
                .ToList();

            var (slnProjects, ProjectConfigurationPlatforms, SolutionConfigurationPlatforms) = slnHelper.ParseLines(lines);

            var targetProjectNames = projectNames
                .Split(',')
                .Select(it => it.Trim())
                .ToImmutableArray();

            var added = new HashSet<string>();

            foreach (var project in slnProjects
                .Where(one => targetProjectNames.Contains("*") || targetProjectNames.Contains(one.Name))
            )
            {
                // {86DB31C6-B393-3617-A8FD-885B2E605A2D}.Debug|ARM64.ActiveCfg = Debug|ARM64
                // {86DB31C6-B393-3617-A8FD-885B2E605A2D}.Debug|ARM64.Build.0 = Debug|ARM64
                foreach (var pair in ProjectConfigurationPlatforms
                    .Where(pair => true
                        && pair.Key.StartsWith(project.ProjKey)
                        && (false
                            || pair.Key.EndsWith($"|{copyFrom}.ActiveCfg")
                            || pair.Key.EndsWith($"|{copyFrom}.Build.0")
                        )
                    )
                )
                {
                    var newKey = pair.Key.Replace($"|{copyFrom}.", $"|{copyTo}.");
                    var newValuePair = pair.Value.Split('|');
                    newValuePair[1] = copyTo;
                    var newValue = string.Join("|", newValuePair);

                    if (!ProjectConfigurationPlatforms.Any(it => it.Key == newKey && it.Value == it.Value))
                    {
                        var at = lines.IndexOf(pair.Line);
                        lines.Insert(at + 1, $"\t\t{newKey} = {newValue}");
                        anyChanged = true;
                    }

                    {
                        var cp = pair.Key.Split('.')[1];
                        var hits = SolutionConfigurationPlatforms
                            .Where(it => it.Key == cp && it.Value == cp)
                            .ToArray();
                        if (hits.Any())
                        {
                            var newCp = newValue;
                            if (!SolutionConfigurationPlatforms.Any(it => it.Key == newCp && it.Value == newCp))
                            {
                                if (added.Add(newCp))
                                {
                                    var at = lines.IndexOf(hits.First().Line);
                                    lines.Insert(at + 1, $"\t\t{newCp} = {newCp}");
                                    anyChanged = true;
                                }
                            }
                        }
                    }
                }

                if (updateProjectFile)
                {
                    var projFile = Path.Combine(Path.GetDirectoryName(slnPath)!, project.File);

                    if (Path.GetExtension(projFile).ToLowerInvariant() == ".vcxproj")
                    {
                        _logger.Info("ProcessingProjFile: {0}", projFile);

                        _cmakeVCXProjSetupArm64x.Run(
                            vcxProjPath: projFile
                        );
                    }
                }
            }

            if (anyChanged)
            {
                slnHelper.SaveLinesToFile(
                    slnPath,
                    lines
                );
            }
        }
    }
}
