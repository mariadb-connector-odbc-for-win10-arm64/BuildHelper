using System.Collections.Immutable;
using System.Xml.Linq;

namespace LibBuildHelper
{
    public class CMakeVCXProjSetupArm64x
    {
        public void Run(string vcxProjPath)
        {
            var doc = XDocument.Load(vcxProjPath);
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
                doc.Save(vcxProjPath);
            }
        }

        private static XElement Clone(XElement source)
        {
            return new XElement(source);
        }

        private delegate (string ARM64Value, string ARM64ECValue) PropertyValueTransformationDelegate(string propertyValue);

        private static string ChangeOption(string options, string from, string to)
        {
            return string.Join(
                " ",
                options
                    .Split(' ')
                    .Select(it => (it == from) ? to : it)
            );
        }
    }
}
