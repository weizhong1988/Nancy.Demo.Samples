﻿namespace Nancy.Demo.Samples.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Nancy.Helpers;
    using MarkdownSharp;
    using Nancy.Demo.Samples.Models;
    using Net;

    /// <summary>
    /// Default implementation of the <see cref="IDemoModelFactory"/> interface. This implemenation will
    /// look for repositories that followes the Name.Demo.ProjectName naming convention.
    /// </summary>
    public class DefaultDemoModelFactory : IDemoModelFactory
    {
        private readonly IGitHubHttpClient client;
        private readonly IGitHubFileContentExtractor githubFileContentExtractor;
        private readonly IRepositoryNugetChecker repositoryNugetChecker;

        private readonly Regex assemblyVersionPattern = new Regex(@"AssemblyVersion\(\""(?<version>[0-9\.]*)\""\)", RegexOptions.Compiled);
        private readonly Regex nugetPackagesPattern = new Regex("(?:id=\"(?<name>Nancy(?:.+?)?)\" version=\"(?<version>.+?)\")", RegexOptions.Multiline | RegexOptions.Compiled);
        private readonly Regex readMePattern = new Regex(@"[\`]{3}(?<language>.+?)(?:[\n\r]+)(?<content>.+?)(?:[\n\r]+)[\`]{3}", RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Creates a new instance of the <see cref="DefaultDemoModelFactory"/> class, using the provided
        /// <paramref name="client"/>, <paramref name="githubFileContentExtractor"/> and <paramref name="repositoryNugetChecker"/>.
        /// </summary>
        /// <param name="client">The <see cref="IGitHubHttpClient"/> that should be used to communicate with GitHub.</param>
        /// <param name="githubFileContentExtractor"></param>
        /// <param name="repositoryNugetChecker">The <see cref="IRepositoryNugetChecker"/> that should be used to verify that a Nuget is available for the demo.</param>
        public DefaultDemoModelFactory(IGitHubHttpClient client, IGitHubFileContentExtractor githubFileContentExtractor, IRepositoryNugetChecker repositoryNugetChecker)
        {
            this.client = client;
            this.githubFileContentExtractor = githubFileContentExtractor;
            this.repositoryNugetChecker = repositoryNugetChecker;
        }

        /// <summary>
        /// Retrieves information about Nancy demo projects, from the GitHub account that is
        /// identified with the provided <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The username that should be queried.</param>
        /// <returns>An <see cref="IEnumerable{T}"/>, containing <see cref="DemoModel"/> instances.</returns>
        public IEnumerable<DemoModel> Retrieve(string username)
        {
            var url =
                string.Format("users/{0}/repos", username);

            var data =
                this.client.Get(url);

            return CreateDemosModels(data);
        }

        private IEnumerable<DemoModel> CreateDemosModels(dynamic result)
        {
            var demos = ((IEnumerable<dynamic>)result)
                .Where(repo => repo.name.StartsWith("Nancy.Demo", StringComparison.OrdinalIgnoreCase))
                .Where(repo => ((bool)repo.fork) == false)
                .Select(repo => new DemoModel {
                    Id = Guid.NewGuid().ToString(),
                    Name = repo.name,
                    CreatedAt = DateTime.Parse(repo.created_at),
                    Description = repo.description,
                    Author = repo.owner.login,
                    Gravatar = repo.owner.avatar_url,
                    HasNuget = this.repositoryNugetChecker.IsNugetAvailable(repo.name),
                    IndexedAt = DateTime.Now,
                    LastCommit = DateTime.Parse(repo.updated_at),
                    Url = repo.html_url,
                    Packages = this.ExtractNugetPackages(repo.owner.login, repo.name),
                    Readme = this.ExtractReadMe(repo.owner.login, repo.name),
                    Version = this.ExtractVersion(repo.owner.login, repo.name)
                });

            return GetValid(demos.ToArray());
        }

        private static IEnumerable<DemoModel> GetValid(IEnumerable<DemoModel> demos)
        {
            return demos;
            //.Where(demo => !string.IsNullOrWhiteSpace(demo.Readme))
            //.Where(demo => !string.IsNullOrWhiteSpace(demo.Version))
            //.Where(demo => !string.IsNullOrWhiteSpace(demo.Description));
        }

        private IEnumerable<NugetPackageInformation> ExtractNugetPackages(string owner, string repositoryName)
        {
            var relativeFilePath =
                string.Format("contents/src/{0}/packages.config", repositoryName);

            return this.githubFileContentExtractor.Extract(owner, repositoryName, relativeFilePath, content =>
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return Enumerable.Empty<NugetPackageInformation>();
                }

                return nugetPackagesPattern.Matches(content)
                    .OfType<Match>()
                    .Select(match => new NugetPackageInformation {
                        Name = match.Groups["name"].Value,
                        Version = match.Groups["version"].Value});
            });
        }

        private string ExtractReadMe(string owner, string repositoryName)
        {
            return this.githubFileContentExtractor.Extract(owner, repositoryName, "readme", content =>
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return string.Empty;
                }

                content =
                    HttpUtility.HtmlEncode(content);

                content =
                    readMePattern.Replace(content, capture =>
                    {
                        var value = capture.Groups[0]
                            .Value
                            .Replace(string.Concat("```", capture.Groups["language"]), string.Empty)
                            .Replace("```", string.Empty)
                            .Trim(new[] {'\r', '\n'});

                        return string.Concat("<pre><code>", value, "</code></pre>");
                    });

                var renderer =
                    new Markdown();

                return renderer.Transform(content);
            });
        }


        private string ExtractVersion(string owner, string repositoryName)
        {
            var relativeFilePath =
                string.Format("contents/src/{0}/Properties/AssemblyInfo.cs", repositoryName);

            return this.githubFileContentExtractor.Extract(owner, repositoryName, relativeFilePath, content =>
            {
                return !string.IsNullOrWhiteSpace(content)
                    ? this.assemblyVersionPattern.Match(content).Groups["version"].Value
                    : string.Empty;
            });
        }
    }
}