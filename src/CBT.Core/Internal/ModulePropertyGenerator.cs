﻿using Microsoft.Build.Construction;
using Microsoft.Build.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CBT.Core.Internal
{
    internal sealed class ModulePropertyGenerator
    {
        internal static readonly string ImportRelativePath = Path.Combine("CBT", "Module", "$(MSBuildThisFile)");
        internal static readonly string ModuleConfigPath = Path.Combine("CBT", "Module", "module.config");
        internal static readonly string PropertyNamePrefix = "CBTModule_";
        internal static readonly string PropertyValuePrefix = $"$(NuGetPackagesPath){Path.DirectorySeparatorChar}";
        private readonly CBTTaskLogHelper _log;
        private readonly IDictionary<string, PackageIdentityWithPath> _packages;
        private readonly string _packagesPath;

        public ModulePropertyGenerator(CBTTaskLogHelper logHelper, string packagesPath, params string[] packageConfigPaths)
            : this(new List<INuGetPackageConfigParser>
            {
                new NuGetPackagesConfigParser(),
                new NuGetProjectJsonParser()
            }, packagesPath, packageConfigPaths)
        {
            _log = logHelper;
        }

        public ModulePropertyGenerator(IList<INuGetPackageConfigParser> configParsers, string packagesPath, params string[] packageConfigPaths)
        {
            if (configParsers == null)
            {
                throw new ArgumentNullException(nameof(configParsers));
            }

            if (String.IsNullOrWhiteSpace(packagesPath))
            {
                throw new ArgumentNullException(nameof(packagesPath));
            }

            if (!Directory.Exists(packagesPath))
            {
                throw new DirectoryNotFoundException($"Could not find part of the path '{packagesPath}'");
            }

            if (packageConfigPaths == null)
            {
                throw new ArgumentNullException(nameof(packageConfigPaths));
            }

            _packagesPath = packagesPath;
            _packages = packageConfigPaths
                .SelectMany(i => configParsers
                .SelectMany(parser => parser.GetPackages(packagesPath, i)))
                .ToDictionary(i => $"{i.Id}.{i.Version}", i => i, StringComparer.OrdinalIgnoreCase);
        }

        public bool Generate(string outputPath, string extensionsPath, string[] beforeModuleImports, string[] afterModuleImports)
        {
            _log.LogMessage(MessageImportance.Low, $"Modules:");
            foreach (PackageIdentityWithPath package in _packages.Values)
            {
                _log.LogMessage(MessageImportance.Low, $"  {package.Id} {package.Version}");
            }
            ProjectRootElement project = CreateProjectWithNuGetProperties();

            List<string> modulePaths = _packages.Values.Select(i => Path.Combine(PropertyValuePrefix, i.Path)).ToList();

            if (beforeModuleImports != null)
            {
                foreach (ProjectImportElement import in beforeModuleImports.Where(i => !String.IsNullOrWhiteSpace(i)).Select(project.AddImport))
                {
                    import.Condition = $" Exists('{import.Project}') ";
                }
            }

            AddImports(project, modulePaths);

            if (afterModuleImports != null)
            {
                foreach (ProjectImportElement import in afterModuleImports.Where(i => !String.IsNullOrWhiteSpace(i)).Select(project.AddImport))
                {
                    import.Condition = $" Exists('{import.Project}') ";
                }
            }

            _log.LogMessage(MessageImportance.Low, $"Saving import file '{outputPath}'.");

            project.Save(outputPath);

            foreach (string item in GetModuleExtensions().Select(i => i.Key.Trim()))
            {
                ProjectRootElement extensionProject = ProjectRootElement.Create(Path.Combine(extensionsPath, item));

                AddImports(extensionProject, modulePaths);

                _log.LogMessage(MessageImportance.Low, $"Saving import file '{extensionProject.FullPath}'.");

                extensionProject.Save();
            }

            return true;
        }

        private void AddImports(ProjectRootElement project, IEnumerable<string> modulePaths)
        {
            foreach (ProjectImportElement import in modulePaths.Where(i => !String.IsNullOrWhiteSpace(i)).Select(modulePath => project.AddImport(Path.Combine(modulePath, ImportRelativePath))))
            {
                import.Condition = $" Exists('{import.Project}') ";
            }
        }

        private ProjectRootElement CreateProjectWithNuGetProperties()
        {
            ProjectRootElement project = ProjectRootElement.Create();

            ProjectPropertyGroupElement propertyGroup = project.AddPropertyGroup();

            propertyGroup.SetProperty("MSBuildAllProjects", "$(MSBuildAllProjects);$(MSBuildThisFileFullPath)");
            ProjectPropertyElement nuGetPackagesPathProperty = propertyGroup.AddProperty("NuGetPackagesPath", _packagesPath);

            nuGetPackagesPathProperty.Condition = " '$(NuGetPackagesPath)' == '' ";

            propertyGroup.SetProperty("CBTAllModulePaths", String.Join(";", _packages.Values.Select(i => $"{i.Id}={PropertyValuePrefix}{i.Path}")));

            foreach (PackageIdentityWithPath item in _packages.Values)
            {
                // Generate the property name and value once
                //
                string propertyName = $"{PropertyNamePrefix}{item.Id.Replace(".", "_")}";
                string propertyValue = $"{PropertyValuePrefix}{item.Path}";

                propertyGroup.SetProperty(propertyName, propertyValue);
            }

            return project;
        }

        private IDictionary<string, string> GetModuleExtensions()
        {
            ConcurrentDictionary<string, string> extensionImports = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Parallel.ForEach(_packages.Values, packageInfo =>
            {
                string path = Path.Combine(packageInfo.FullPath, ModuleConfigPath);

                if (File.Exists(path))
                {
                    XDocument document = XDocument.Load(path);

                    XElement extensionImportsElement = document.Root?.Element("extensionImports");

                    if (extensionImportsElement != null)
                    {
                        foreach (string item in extensionImportsElement.Elements("add").Select(i => i.Attribute("name")).Where(i => !String.IsNullOrWhiteSpace(i?.Value)).Select(i => i.Value))
                        {
                            extensionImports.TryAdd(item, packageInfo.Id);
                        }
                    }
                }
            });

            _log.LogMessage(MessageImportance.Low, $"Module extensions:");

            foreach (KeyValuePair<string, string> item in extensionImports)
            {
                _log.LogMessage(MessageImportance.Low, $"  {item.Key} ({item.Value})");
            }

            return extensionImports;
        }
    }
}