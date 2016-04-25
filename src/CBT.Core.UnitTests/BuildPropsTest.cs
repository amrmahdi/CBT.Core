﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Construction;
using NUnit.Framework;
using Shouldly;

namespace CBT.Core.UnitTests
{
    /// <summary>
    /// Tests to verify the core build.props file.
    /// </summary>
    [TestFixture]
    public class BuildPropsTest
    {
        private ProjectRootElement _project;

        [TestFixtureSetUp]
        public void TestInitialize()
        {
            _project = ProjectRootElement.Open("build.props");
        }

        [Test]
        [Description("Verifies that required properties exist in the build.props file.")]
        public void RequiredPropertiesTest()
        {
            IDictionary<string, string> knownProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"CBTCoreAssemblyPath", null},
                {"CBTIntermediateOutputPath", null},
                {"CBTModulePackageConfigPath", @" '$({0})' == '' And '$(CBTLocalPath)' != '' And Exists('$(CBTLocalPath)\CBTModules\packages.config') "},
                {"CBTModuleConfigPath", null},
                {"CBTModulePath", null},
                {"CBTModulePropertiesFile", null},
                {"CBTModuleExtensionsPath", null},
                {"CBTModulePropertyNamePrefix", null},
                {"CBTModulePropertyValuePrefix", null},
                {"CBTModuleImportRelativePaths", null},
                {"CBTModuleImportsBefore", null},
                {"CBTModuleImportsAfter", null},
                {"CBTModuleRestoreTaskName", null},
                {"CBTModuleRestoreCommand", null},
                {"CBTModuleRestoreCommandArguments", null},
                {"CBTModuleRestoreInputs", null},
                {"CBTModulesRestored", " '$(BuildingInsideVisualStudio)' != 'true' And '$({0})' != 'true' And Exists('$(CBTCoreAssemblyPath)') "}
            };

            foreach (var knownProperty in knownProperties)
            {
                var property = _project.Properties.FirstOrDefault(i => i.Name.Equals(knownProperty.Key, StringComparison.OrdinalIgnoreCase));

                property.ShouldNotBe(null);

                property.Condition.ShouldBe(string.Format(knownProperty.Value ?? " '$({0})' == '' ", property.Name));
            }

            PropertyShouldBe("MSBuildAllProjects", "$(MSBuildAllProjects);$(MSBuildThisFileFullPath)");
            PropertyShouldBe("EnlistmentRoot", @"$(EnlistmentRoot.TrimEnd('\\'))");

            var globalPathProperty = _project.Properties.Where(i => i.Name.Equals("CBTGlobalPath")).ToList();
            globalPathProperty.Count.ShouldBe(2);
            globalPathProperty[0].Condition.ShouldBe(String.Format(" '$({0})' == '' ", globalPathProperty[0].Name), Case.Insensitive);
            globalPathProperty[0].Value.ShouldBe("$(MSBuildThisFileDirectory)", Case.Insensitive);
            globalPathProperty[1].Value.ShouldBe(String.Format(@"$({0}.TrimEnd('\\'))", globalPathProperty[0].Name), Case.Insensitive);

            var localPathProperty = _project.Properties.Where(i => i.Name.Equals("CBTLocalPath")).ToList();
            localPathProperty.Count.ShouldBe(2);
            localPathProperty[0].Condition.ShouldBe(String.Format(@" '$({0})' == '' And Exists('$([System.IO.Path]::GetDirectoryName($({1})))\Local') ", localPathProperty[0].Name, globalPathProperty[0].Name), Case.Insensitive);
            localPathProperty[0].Value.ShouldBe(@"$([System.IO.Path]::GetDirectoryName($(CBTGlobalPath)))\Local", Case.Insensitive);
            localPathProperty[1].Value.ShouldBe(String.Format(@"$({0}.TrimEnd('\\'))", localPathProperty[1].Name), Case.Insensitive);

            var localBuildExtensionsPathProperty = _project.Properties.Where(i => i.Name.Equals("CBTLocalBuildExtensionsPath")).ToList();
            localBuildExtensionsPathProperty.Count.ShouldBe(2);

            localBuildExtensionsPathProperty[0].Condition.ShouldBe(String.Format(@" '$({0})' == '' And '$({1})' != '' And Exists('$({1})\Extensions\$(MSBuildToolsVersion)') ", localBuildExtensionsPathProperty[0].Name, localPathProperty[0].Name));
            localBuildExtensionsPathProperty[0].Value.ShouldBe(String.Format(@"$({0})\Extensions\$(MSBuildToolsVersion)", localPathProperty[0].Name));

            localBuildExtensionsPathProperty[1].Condition.ShouldBe(String.Format(@" '$({0})' == '' And '$({1})' != '' And Exists('$({1})\Extensions') ", localBuildExtensionsPathProperty[1].Name, localPathProperty[0].Name));
            localBuildExtensionsPathProperty[1].Value.ShouldBe(String.Format(@"$({0})\Extensions", localPathProperty[0].Name));
        }

        [Test]
        [Description("Verifies that the RestoreCBTModules target and RestoreModules task are properly defined.")]
        public void RestoreCBTModulesTargetTest()
        {
            _project.InitialTargets.ShouldBe("RestoreCBTModules");

            var target = _project.Targets.FirstOrDefault(i => i.Name.Equals("RestoreCBTModules", StringComparison.OrdinalIgnoreCase));

            target.ShouldNotBe(null);

            target.Condition.ShouldBe(" '$(CBTModulesRestored)' == '' ");

            target.Inputs.ShouldBe("$(CBTModuleRestoreInputs)");

            target.Outputs.ShouldBe("$(CBTModulePropertiesFile)");

            var task = target.Tasks.FirstOrDefault(i => i.Name.Equals("RestoreModules"));

            task.ShouldNotBe(null);

            task.Parameters.ShouldBe(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"AfterImports", "$(CBTModuleImportsAfter.Split(';'))"},
                {"BeforeImports", "$(CBTModuleImportsBefore.Split(';'))"},
                {"ConfigPath", "$(CBTModuleConfigPath)"},
                {"ExtensionsPath", "$(CBTModuleExtensionsPath)"},
                {"ImportRelativePaths", "$(CBTModuleImportRelativePaths.Split(';'))"},
                {"ImportsFile", "$(CBTModulePropertiesFile)"},
                {"PackageConfig", "$(CBTModulePackageConfigPath)"},
                {"PackagesPath", "$(NuGetPackagesPath)"},
                {"PropertyNamePrefix", "$(CBTModulePropertyNamePrefix)"},
                {"PropertyValuePrefix", "$(CBTModulePropertyValuePrefix)"},
                {"RestoreCommand", "$(CBTModuleRestoreCommand)"},
                {"RestoreCommandArguments", "$(CBTModuleRestoreCommandArguments)"}
            });

            var propertyGroup = target.PropertyGroups.LastOrDefault();

            propertyGroup.ShouldNotBe(null);

            propertyGroup.Location.ShouldBeGreaterThan(task.Location, "<PropertyGroup /> should come after <RestoreModules /> task");

            var property = propertyGroup.Properties.FirstOrDefault(i => i.Name.Equals("CBTModulesRestored", StringComparison.OrdinalIgnoreCase));

            property.ShouldNotBe(null);

            property.Condition.ShouldBe(String.Format(" '$({0})' != 'true' ", property.Name));

            property.Value.ShouldBe(true.ToString(), Case.Insensitive);

            var usingTask = _project.UsingTasks.FirstOrDefault(i => i.TaskName.Equals("RestoreModules", StringComparison.OrdinalIgnoreCase));

            usingTask.ShouldNotBe(null);

            usingTask.AssemblyFile.ShouldBe("$(CBTCoreAssemblyPath)", Case.Insensitive);
        }

        [Test]
        [Description("Verifies that imports are correct.")]
        public void ImportsTest()
        {
            var beforeImport = _project.Imports.FirstOrDefault(i => i.Project.Equals(@"$(CBTLocalBuildExtensionsPath)\Before.$(MSBuildThisFile)", StringComparison.OrdinalIgnoreCase));
            
            beforeImport.ShouldNotBe(null);

            beforeImport.Condition.ShouldBe(@" '$(CBTLocalBuildExtensionsPath)' != '' And Exists('$(CBTLocalBuildExtensionsPath)\Before.$(MSBuildThisFile)') ");

            var firstPropertyGroup = _project.PropertyGroups.First();
            var secondPropertyGroup = _project.PropertyGroups.Skip(1).First();

            beforeImport.Location.ShouldBeGreaterThan(firstPropertyGroup.Location, @"The import of '$(CBTLocalBuildExtensionsPath)\Before.$(MSBuildThisFile)' should come after the first <PropertyGroup />");

            secondPropertyGroup.Location.ShouldBeGreaterThan(beforeImport.Location, @"The import of '$(CBTLocalBuildExtensionsPath)\Before.$(MSBuildThisFile)' should come before the second <PropertyGroup />");

            var afterImport = _project.Imports.FirstOrDefault(i => i.Project.Equals(@"$(CBTLocalBuildExtensionsPath)\After.$(MSBuildThisFile)", StringComparison.OrdinalIgnoreCase));

            afterImport.ShouldNotBe(null);

            afterImport.Condition.ShouldBe(@" '$(CBTLocalBuildExtensionsPath)' != '' And Exists('$(CBTLocalBuildExtensionsPath)\After.$(MSBuildThisFile)') ");

            var lastChild = _project.Children.Last();

            afterImport.ShouldBe(lastChild, @"The last element should be the import of '$(CBTLocalBuildExtensionsPath)\After.$(MSBuildThisFile'");

            var moduleImport = _project.Imports.FirstOrDefault(i => i.Project.Equals("$(CBTModulePropertiesFile)", StringComparison.OrdinalIgnoreCase));

            moduleImport.ShouldNotBe(null);
            moduleImport.Condition.ShouldBe(" ('$(CBTModulesRestored)' == 'true' Or '$(BuildingInsideVisualStudio)' == 'true') And Exists('$(CBTModulePropertiesFile)') ");

            moduleImport.Location.ShouldBeGreaterThan(secondPropertyGroup.Location, "The import of '$(CBTModulePropertiesFile)' should come after the second property group");
        }

        private void PropertyShouldBe(string name, string value, Case @case = Case.Insensitive)
        {
            var property = _project.Properties.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            property.ShouldNotBe(null);

            property.Value.ShouldBe(value, @case);
        }
    }
}