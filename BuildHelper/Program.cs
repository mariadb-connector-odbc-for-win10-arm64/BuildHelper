using BuildHelper.Helpers;
using CommandLine;
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

        private delegate (string ARM64Value, string ARM64ECValue) PropertyValueTransformationDelegate(string propertyValue);

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
            var logger = LogManager.GetLogger("SlnCloneCp");

            var anyChanged = false;

            var slnHelper = new SlnHelper();

            var slnPath = Path.GetFullPath(o.SlnPath);

            logger.Info("ProcessingSlnFile: {0}", slnPath);

            var lines = slnHelper.LoadLinesFromFile(slnPath)
                .ToList();

            var (slnProjects, ProjectConfigurationPlatforms, SolutionConfigurationPlatforms) = slnHelper.ParseLines(lines);

            var targetProjectNames = o.ProjectNames
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
                            || pair.Key.EndsWith($"|{o.CopyFrom}.ActiveCfg")
                            || pair.Key.EndsWith($"|{o.CopyFrom}.Build.0")
                        )
                    )
                )
                {
                    var newKey = pair.Key.Replace($"|{o.CopyFrom}.", $"|{o.CopyTo}.");
                    var newValuePair = pair.Value.Split('|');
                    newValuePair[1] = o.CopyTo;
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

                if (o.UpdateProjectFile)
                {
                    var projFile = Path.Combine(Path.GetDirectoryName(slnPath)!, project.File);

                    if (Path.GetExtension(projFile).ToLowerInvariant() == ".vcxproj")
                    {
                        logger.Info("ProcessingProjFile: {0}", projFile);

                        DoCMakeVCXProjSetupArm64x(new CMakeVCXProjSetupArm64xOptions()
                        {
                            VcxProjPath = projFile,
                        });
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
            return 0;
        }

        private static int DoCMakeVCXProjSetupArm64x(CMakeVCXProjSetupArm64xOptions o)
        {
            var doc = XDocument.Load(o.VcxProjPath);
            var xmlns = XNamespace.Get("http://schemas.microsoft.com/developer/msbuild/2003");
            var anyChanged = false;

            {
                var ProjectConfigurations = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "ItemGroup")
                    .Elements(xmlns + "ProjectConfiguration")
                    .ToImmutableArray();

                foreach (XElement arm64ProjectConfiguration in ProjectConfigurations)
                {
                    // <ProjectConfiguration Include="Debug|ARM64">
                    // <ProjectConfiguration Include="Release|ARM64">
                    // <ProjectConfiguration Include="MinSizeRel|ARM64">
                    // <ProjectConfiguration Include="RelWithDebInfo|ARM64">
                    var include = arm64ProjectConfiguration.Attribute("Include")?.Value ?? "";
                    if (include.EndsWith("|ARM64"))
                    {
                        var arm64ECInclude = include.Replace("|ARM64", "|ARM64EC");
                        if (!ProjectConfigurations.Any(it => it.Attribute("Include")?.Value == arm64ECInclude))
                        {
                            var arm64ECConfiguration = Clone(arm64ProjectConfiguration);
                            arm64ECConfiguration.SetAttributeValue(
                                "Include",
                                arm64ECInclude
                            );
                            arm64ECConfiguration
                                .Element(xmlns + "Platform")!
                                .SetValue("ARM64EC");
                            arm64ProjectConfiguration.AddAfterSelf(arm64ECConfiguration);
                            anyChanged = true;
                        }
                    }
                }
            }

            {
                var ConfigurationPropertyGroups = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "PropertyGroup")
                    .Where(it => it.Attribute("Label")?.Value == "Configuration")
                    .ToImmutableArray();

                // <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|ARM64'" Label="Configuration">
                // <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|ARM64'" Label="Configuration">
                // <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='MinSizeRel|ARM64'" Label="Configuration">
                // <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='RelWithDebInfo|ARM64'" Label="Configuration">
                foreach (XElement arm64Configuration in ConfigurationPropertyGroups)
                {
                    var arm64Condition = arm64Configuration.Attribute("Condition")?.Value ?? "";
                    if (arm64Condition.Contains("|ARM64'"))
                    {
                        var arm64ECCondition = arm64Condition.Replace("|ARM64'", "|ARM64EC'");
                        if (!ConfigurationPropertyGroups.Any(it => it.Attribute("Condition")?.Value == arm64ECCondition))
                        {
                            var arm64ECConfigurationPropertyGroup = Clone(arm64Configuration);
                            arm64ECConfigurationPropertyGroup.SetAttributeValue("Condition", arm64ECCondition);
                            arm64Configuration.AddAfterSelf(arm64ECConfigurationPropertyGroup);
                            anyChanged = true;
                        }
                    }
                }
            }

            {
                var ProjectConfigurations = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "ItemGroup")
                    .Elements(xmlns + "ProjectConfiguration")
                    .ToImmutableArray();

                foreach (XElement arm64ProjectConfiguration in ProjectConfigurations)
                {
                    var include = arm64ProjectConfiguration.Attribute("Include")?.Value ?? "";
                    if (include.EndsWith("|ARM64EC"))
                    {
                        // <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|ARM64EC'">
                        //   <BuildAsX>true</BuildAsX>
                        // </PropertyGroup>
                        var arm64ECCondition = $"'$(Configuration)|$(Platform)'=='{include}'";
                        var BuildAsXs = doc
                            .Element(xmlns + "Project")!
                            .Elements(xmlns + "PropertyGroup")
                            .Where(it => it.Attribute("Condition")?.Value == arm64ECCondition)
                            .Elements(xmlns + "BuildAsX")
                            .ToImmutableArray();

                        if (BuildAsXs.Any())
                        {
                            foreach (var secondary in BuildAsXs.Skip(1))
                            {
                                secondary.Remove();
                                anyChanged = true;
                            }

                            var primary = BuildAsXs.First();
                            if (primary.Value != "true")
                            {
                                primary.SetValue("true");
                                anyChanged = true;
                            }
                        }
                        else
                        {
                            var PropertyGroups = doc
                                .Element(xmlns + "Project")!
                                .Elements(xmlns + "PropertyGroup")
                                .Where(it => it.Attribute("Condition")?.Value == arm64ECCondition)
                                .ToImmutableArray();

                            var PropertyGroup = PropertyGroups.First();
                            PropertyGroup.Add(
                                new XElement(xmlns + "BuildAsX", "true")
                            );
                            anyChanged = true;
                        }
                    }
                }
            }

            {
                var operators = new List<(string ElementName, PropertyValueTransformationDelegate Transformer)>();
                // <OutDir Condition="'$(Configuration)|$(Platform)'=='Debug|ARM64'">V:\mariadb-connector-odbc-for-win10-arm64\arm64x-build\libmariadb\external\zlib\Debug\</OutDir>
                operators.Add((
                    "OutDir",
                    (propertyValue) =>
                    {
                        if (propertyValue == "$(Platform)\\$(Configuration)\\$(ProjectName)\\")
                        {
                            return (
                                ARM64Value: propertyValue,
                                ARM64ECValue: propertyValue
                            );
                        }
                        else
                        {
                            return (
                                ARM64Value: Path.Combine(propertyValue, "ARM64") + "\\",
                                ARM64ECValue: propertyValue
                            );
                        }
                    }
                ));
                // <IntDir Condition="'$(Configuration)|$(Platform)'=='Debug|ARM64'">zlib.dir\Debug\</IntDir>
                operators.Add((
                    "IntDir",
                    (propertyValue) =>
                    {
                        if (propertyValue == "$(Platform)\\$(Configuration)\\$(ProjectName)\\")
                        {
                            return (
                                ARM64Value: propertyValue,
                                ARM64ECValue: propertyValue
                            );
                        }
                        else
                        {
                            return (
                                ARM64Value: Path.Combine(propertyValue, "ARM64") + "\\",
                                ARM64ECValue: Path.Combine(propertyValue, "ARM64EC") + "\\"
                            );
                        }
                    }
                ));
                // <TargetName Condition="'$(Configuration)|$(Platform)'=='Debug|ARM64'">zlibd</TargetName>
                operators.Add((
                    "TargetName",
                    (propertyValue) =>
                    {
                        return (
                            ARM64Value: propertyValue,
                            ARM64ECValue: propertyValue
                        );
                    }
                ));
                // <TargetExt Condition="'$(Configuration)|$(Platform)'=='Debug|ARM64'">.lib</TargetExt>
                operators.Add((
                    "TargetExt",
                    (propertyValue) =>
                    {
                        return (
                            ARM64Value: propertyValue,
                            ARM64ECValue: propertyValue
                        );
                    }
                ));

                foreach (var op in operators)
                {
                    var elements = doc
                        .Element(xmlns + "Project")!
                        .Elements(xmlns + "PropertyGroup")
                        .Elements(xmlns + op.ElementName)
                        .ToImmutableArray();

                    foreach (XElement arm64Element in elements)
                    {
                        var arm64Condition = arm64Element.Attribute("Condition")?.Value ?? "";
                        if (arm64Condition.Contains("|ARM64'"))
                        {
                            var arm64ECCondition = arm64Condition.Replace("|ARM64'", "|ARM64EC'");
                            if (!elements.Any(it => it.Attribute("Condition")?.Value == arm64ECCondition))
                            {
                                var arm64ECElement = Clone(arm64Element);
                                arm64ECElement.SetAttributeValue("Condition", arm64ECCondition);
                                var result = op.Transformer(arm64Element.Value);
                                arm64Element.SetValue(result.ARM64Value);
                                arm64ECElement.SetValue(result.ARM64ECValue);
                                arm64Element.AddAfterSelf(arm64ECElement);
                                anyChanged = true;
                            }
                        }
                    }
                }
            }

            {
                // <ItemDefinitionGroup Condition="'$(Configuration)|$(Platform)'=='Release|ARM64'">

                var ItemDefinitionGroups = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "ItemDefinitionGroup")
                    .ToImmutableArray();

                foreach (XElement arm64ItemDefinitionGroup in ItemDefinitionGroups)
                {
                    var arm64Condition = arm64ItemDefinitionGroup.Attribute("Condition")?.Value ?? "";
                    if (arm64Condition.Contains("|ARM64'"))
                    {
                        var arm64ECCondition = arm64Condition.Replace("|ARM64'", "|ARM64EC'");
                        if (!ItemDefinitionGroups.Any(it => it.Attribute("Condition")?.Value == arm64ECCondition))
                        {
                            var arm64ECItemDefinitionGroup = Clone(arm64ItemDefinitionGroup);
                            arm64ECItemDefinitionGroup.SetAttributeValue("Condition", arm64ECCondition);
                            arm64ItemDefinitionGroup.AddAfterSelf(arm64ECItemDefinitionGroup);
                            anyChanged = true;
                        }
                    }
                }
            }

            {
                var ItemDefinitionGroups = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "ItemDefinitionGroup")
                    .ToImmutableArray();

                foreach (XElement arm64ItemDefinitionGroup in ItemDefinitionGroups)
                {
                    var arm64Condition = arm64ItemDefinitionGroup.Attribute("Condition")?.Value ?? "";
                    if (arm64Condition.Contains("|ARM64EC'"))
                    {
                        if (arm64ItemDefinitionGroup.Element(xmlns + "Lib") is XElement Lib)
                        {
                            if (Lib.Element(xmlns + "AdditionalOptions") is XElement AdditionalOptions)
                            {
                                var value = AdditionalOptions.Value ?? "";
                                var newValue = ChangeOption(value, "/machine:ARM64", "/machine:ARM64X");
                                if (value != newValue)
                                {
                                    AdditionalOptions.SetValue(newValue);
                                    anyChanged = true;
                                }
                            }
                        }

                        if (arm64ItemDefinitionGroup.Element(xmlns + "Link") is XElement Link)
                        {
                            if (Link.Element(xmlns + "AdditionalOptions") is XElement AdditionalOptions)
                            {
                                var value = AdditionalOptions.Value ?? "";
                                var newValue = ChangeOption(value, "/machine:ARM64", "/machine:ARM64X");
                                if (value != newValue)
                                {
                                    AdditionalOptions.SetValue(newValue);
                                    anyChanged = true;
                                }
                            }
                        }
                    }
                }
            }

            {
                var Objects = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "ItemGroup")
                    .Elements(xmlns + "Object")
                    .ToImmutableArray();

                foreach (var Object in Objects)
                {
                    var include = Object.Attribute("Include")?.Value ?? "";
                    var newInclude = include;
                    var includeParts = include.Split('/', '\\').ToList();
                    if (true
                        && include.EndsWith(".obj", StringComparison.InvariantCultureIgnoreCase)
                        && 3 <= includeParts.Count && includeParts[includeParts.Count - 2] == "$(Configuration)"
                    )
                    {
                        includeParts.Insert(includeParts.Count - 1, "$(Platform)");
                        newInclude = string.Join("\\", includeParts);
                    }

                    if (include != newInclude)
                    {
                        Object.Attribute("Include")!.SetValue(newInclude);
                        anyChanged = true;
                    }
                }
            }

            {
                var elements = doc
                    .Element(xmlns + "Project")!
                    .Elements(xmlns + "ItemGroup")
                    .Elements(xmlns + "CustomBuild")
                    .Elements()
                    .ToImmutableArray();

                foreach (var element in elements)
                {
                    // <Message Condition="'$(Configuration)|$(Platform)'=='Release|ARM64'">Building Custom Rule V:/mariadb-connector-odbc-for-win10-arm64/mariadb-connector-odbc/packaging/windows/CMakeLists.txt</Message>

                    var arm64Condition = element.Attribute("Condition")?.Value ?? "";
                    if (arm64Condition.Contains("|ARM64'"))
                    {
                        var arm64ECCondition = arm64Condition.Replace("|ARM64'", "|ARM64EC'");
                        if (!elements.Any(it => it.Attribute("Condition")?.Value == arm64ECCondition))
                        {
                            var arm64ECElement = Clone(element);
                            arm64ECElement.SetAttributeValue("Condition", arm64ECCondition);
                            element.AddAfterSelf(arm64ECElement);
                            anyChanged = true;
                        }
                    }
                }
            }

            if (anyChanged)
            {
                doc.Save(o.VcxProjPath);
            }
            return 0;
        }

        private static string ChangeOption(string options, string from, string to)
        {
            return string.Join(
                " ",
                options
                    .Split(' ')
                    .Select(it => (it == from) ? to : it)
            );
        }

        private static XElement Clone(XElement source)
        {
            return new XElement(source);
        }
    }
}
