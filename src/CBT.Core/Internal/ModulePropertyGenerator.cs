﻿using Microsoft.Build.Construction;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CBT.Core.Internal
{
    internal sealed class ModulePropertyGenerator
    {
        /// <summary>
        /// The name of the 'ID' attribute in the NuGet packages.config.
        /// </summary>
        private const string NuGetPackagesConfigIdAttributeName = "id";

        /// <summary>
        /// The name of the &lt;package /&gt; element in th NuGet packages.config.
        /// </summary>
        private const string NuGetPackagesConfigPackageElementName = "package";

        /// <summary>
        /// The name of the 'Version' attribute in the NuGet packages.config.
        /// </summary>
        private const string NuGetPackagesConfigVersionAttributeName = "version";

        private readonly string[] _packageConfigPaths;
        private readonly IDictionary<string, PackageInfo> _packages;
        private readonly string _packagesPath;

        public ModulePropertyGenerator(string packagesPath, params string[] packageConfigPaths)
        {
            if (String.IsNullOrWhiteSpace(packagesPath))
            {
                throw new ArgumentNullException("packagesPath");
            }

            if (!Directory.Exists(packagesPath))
            {
                throw new DirectoryNotFoundException(String.Format(CultureInfo.CurrentCulture, "Could not find part of the path '{0}'", packagesPath));
            }

            if (packageConfigPaths == null)
            {
                throw new ArgumentNullException("packageConfigPaths");
            }

            _packagesPath = packagesPath;
            _packageConfigPaths = packageConfigPaths;

            _packages = ParsePackages();
        }

        public bool Generate(string outputPath, string extensionsPath, string moduleConfigPath, string propertyNamePrefix, string propertyValuePrefix, string[] importRelativePaths, string[] beforeModuleImports, string[] afterModuleImports)
        {
            ProjectRootElement project = CreateProjectWithNuGetProperties(propertyNamePrefix, propertyValuePrefix);

            var properties = project.Properties.Where(i => i.Name.StartsWith(propertyNamePrefix)).Select(i => String.Format("$({0})", i.Name)).ToList();

            if (beforeModuleImports != null)
            {
                foreach (var import in beforeModuleImports.Where(i => !String.IsNullOrWhiteSpace(i)).Select(project.AddImport))
                {
                    import.Condition = String.Format(" Exists('{0}') ", import.Project);
                }
            }

            AddImports(project, importRelativePaths, properties);

            if (afterModuleImports != null)
            {
                foreach (var import in afterModuleImports.Where(i => !String.IsNullOrWhiteSpace(i)).Select(project.AddImport))
                {
                    import.Condition = String.Format(" Exists('{0}') ", import.Project);
                }
            }

            project.Save(outputPath);

            Parallel.ForEach(GetModuleExtensions(moduleConfigPath), i =>
            {
                var extensionProject = ProjectRootElement.Create(Path.Combine(extensionsPath, i.Key.Trim()));

                AddImports(extensionProject, importRelativePaths, properties);

                extensionProject.Save();
            });

            return true;
        }

        private void AddImports(ProjectRootElement project, IEnumerable<string> importRelativePaths, IEnumerable<string> modulePaths)
        {
            foreach (var import in modulePaths.Where(i => !String.IsNullOrWhiteSpace(i)).SelectMany(modulePath => importRelativePaths.Where(i => !String.IsNullOrWhiteSpace(i)).Select(importRelativePath => project.AddImport(Path.Combine(modulePath, importRelativePath)))))
            {
                import.Condition = String.Format("Exists('{0}')", import.Project);
            }
        }

        private ProjectRootElement CreateProjectWithNuGetProperties(string propertyNamePrefix, string propertyValuePrefix)
        {
            ProjectRootElement project = ProjectRootElement.Create();

            ProjectPropertyGroupElement propertyGroup = project.AddPropertyGroup();

            propertyGroup.SetProperty("MSBuildAllProjects", "$(MSBuildAllProjects);$(MSBuildThisFileFullPath)");

            foreach (var item in _packages.Values)
            {
                // Generate the property name and value once
                //
                string propertyName = String.Format(CultureInfo.CurrentCulture, "{0}{1}", propertyNamePrefix, item.Id.Replace(".", "_"));
                string propertyValue = String.Format(CultureInfo.CurrentCulture, "{0}{1}.{2}", propertyValuePrefix, item.Id, item.VersionString);

                propertyGroup.SetProperty(propertyName, propertyValue);
            }

            return project;
        }

        private IDictionary<string, string> GetModuleExtensions(string moduleConfigPath)
        {
            ConcurrentDictionary<string, string> extensionImports = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Parallel.ForEach(_packages.Values, packageInfo =>
            {
                var path = Path.Combine(packageInfo.Path, moduleConfigPath);

                if (File.Exists(path))
                {
                    XDocument document = XDocument.Load(path);

                    if (document.Root != null)
                    {
                        var extensionImportsElement = document.Root.Element("extensionImports");

                        if (extensionImportsElement != null)
                        {
                            foreach (var item in extensionImportsElement.Elements("add").Select(i => i.Attribute("name")).Where(i => i != null && !String.IsNullOrWhiteSpace(i.Value)).Select(i => i.Value))
                            {
                                extensionImports.TryAdd(item, packageInfo.Id);
                            }
                        }
                    }
                }
            });

            return extensionImports;
        }

        private IDictionary<string, PackageInfo> ParsePackages()
        {
            IDictionary<string, PackageInfo> packages = new Dictionary<string, PackageInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (string packageConfigPath in _packageConfigPaths.Where(i => !String.IsNullOrWhiteSpace(i) && File.Exists(i)))
            {
                XDocument document = XDocument.Load(packageConfigPath);

                if (document.Root != null)
                {
                    foreach (var item in document.Root.Elements(NuGetPackagesConfigPackageElementName).Select(i => new
                    {
                        Id = i.Attribute(NuGetPackagesConfigIdAttributeName) == null ? null : i.Attribute(NuGetPackagesConfigIdAttributeName).Value,
                        Version = i.Attribute(NuGetPackagesConfigVersionAttributeName) == null ? null : i.Attribute(NuGetPackagesConfigVersionAttributeName).Value,
                    }))
                    {
                        // Skip packages that are missing an 'id' or 'version' attribute or if they specified value is an empty string
                        //
                        if (item.Id == null || item.Version == null ||
                            String.IsNullOrWhiteSpace(item.Id) ||
                            String.IsNullOrWhiteSpace(item.Version))
                        {
                            continue;
                        }

                        PackageInfo packageInfo = new PackageInfo(item.Id, item.Version, Path.Combine(_packagesPath, String.Format("{0}.{1}", item.Id, item.Version)));

                        if (packages.ContainsKey(packageInfo.Id))
                        {
                            packages[packageInfo.Id] = packageInfo;
                        }
                        else
                        {
                            packages.Add(packageInfo.Id, packageInfo);
                        }
                    }
                }
            }

            return packages;
        }
    }
}