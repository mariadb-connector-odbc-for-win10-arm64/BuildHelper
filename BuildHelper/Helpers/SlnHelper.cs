using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BuildHelper.Helpers
{
    internal class SlnHelper
    {
        internal IEnumerable<string> LoadLinesFromFile(string slnFile)
        {
            return Encoding.Latin1.GetString(File.ReadAllBytes(slnFile))
                .Replace("\r\n", "\n")
                .Split('\n');
        }

        internal void SaveLinesToFile(string slnPath, List<string> lines)
        {
            File.WriteAllBytes(
                slnPath,
                Encoding.Latin1.GetBytes(string.Join("\r\n", lines))
            );
        }

        internal (List<SlnProject> SlnProjects, List<SlnKeyValue> ProjectConfigurationPlatforms, List<SlnKeyValue> SolutionConfigurationPlatforms) ParseLines(IEnumerable<string> lines)
        {
            var slnProjects = new List<SlnProject>();
            var ProjectConfigurationPlatforms = new List<SlnKeyValue>();
            var SolutionConfigurationPlatforms = new List<SlnKeyValue>();

            var state = 0;

            // Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "ALL_BUILD", "ALL_BUILD.vcxproj", "{86DB31C6-B393-3617-A8FD-885B2E605A2D}"
            foreach (var line in lines)
            {
                var match = Regex.Match(
                    line,
                    "^\\s*Project\\s*\\(\\s*\"(?<id>.+?)\"\\s*\\)\\s*=\\s*\"(?<name>.+?)\"\\s*,\\s*\"(?<file>.+?)\"\\s*,\\s*\"(?<projkey>.+?)\"\\s*"
                );
                if (match.Success)
                {
                    var id = match.Groups["id"].Value;
                    var name = match.Groups["name"].Value;
                    var file = match.Groups["file"].Value;
                    var projKey = match.Groups["projkey"].Value;
                    slnProjects.Add(new SlnProject(id, name, file, projKey, line));
                }

                if (false) { }
                // GlobalSection(SolutionConfigurationPlatforms) = preSolution
                else if (Regex.IsMatch(line, "^\\s*GlobalSection\\s*\\(\\s*SolutionConfigurationPlatforms\\s*\\)\\s*=\\s*preSolution\\s*$"))
                {
                    state = 1;
                }
                // GlobalSection(SolutionConfigurationPlatforms) = preSolution
                else if (Regex.IsMatch(line, "^\\s*GlobalSection\\s*\\(\\s*ProjectConfigurationPlatforms\\s*\\)\\s*=\\s*postSolution\\s*$"))
                {
                    state = 2;
                }
                // EndGlobalSection
                else if (Regex.IsMatch(line, "^\\s*EndGlobalSection\\s*$"))
                {
                    state = 0;
                }
                else if (state == 1)
                {
                    // Debug|ARM64 = Debug|ARM64
                    var pair = line.Split('=', 2);
                    if (pair.Length == 2)
                    {
                        SolutionConfigurationPlatforms.Add(new SlnKeyValue(
                            pair[0].Trim(),
                            pair[1].Trim(),
                            line
                        ));
                    }
                }
                else if (state == 2)
                {
                    // {86DB31C6-B393-3617-A8FD-885B2E605A2D}.Debug|ARM64.ActiveCfg = Debug|ARM64
                    // {86DB31C6-B393-3617-A8FD-885B2E605A2D}.Debug|ARM64.Build.0 = Debug|ARM64
                    var pair = line.Split('=', 2);
                    if (pair.Length == 2)
                    {
                        ProjectConfigurationPlatforms.Add(new SlnKeyValue(
                            pair[0].Trim(),
                            pair[1].Trim(),
                            line
                        ));
                    }
                }
            }

            return (slnProjects, ProjectConfigurationPlatforms, SolutionConfigurationPlatforms);
        }
    }
}
