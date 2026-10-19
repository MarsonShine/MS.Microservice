const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");

type PackageReference = {
  include: string;
  version: string | null;
};

type ProjectInfo = {
  path: string;
  sdk: string | null;
  targetFrameworks: string[];
  nullable: string | null;
  implicitUsings: string | null;
  isTestProject: boolean;
  aspNetCore: string[];
  efCore: string[];
  analyzers: string[];
  testPackages: string[];
};

const SKIP_DIRS = new Set([".git", ".vs", "bin", "obj", "node_modules", ".venv", "venv", "dist", "build"]);

// ponytail: regex XML scan is enough here; switch to MSBuild or Roslyn only if false positives hurt.

function stripBom(text: string): string {
  return text.replace(/^\uFEFF/, "");
}

function parseArgs(): { repoPath: string; selfCheck: boolean } {
  if (process.argv.includes("--self-check")) {
    return { repoPath: process.cwd(), selfCheck: true };
  }

  const args = process.argv.slice(2);
  let repoPath = process.cwd();

  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg.startsWith("--path=")) {
      repoPath = arg.slice("--path=".length);
      continue;
    }

    if (arg === "--path" && args[index + 1]) {
      repoPath = args[index + 1];
      index += 1;
    }
  }

  return { repoPath: path.resolve(repoPath), selfCheck: false };
}

async function walk(dirPath: string, files: string[] = []): Promise<string[]> {
  const entries = await fs.readdir(dirPath, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name)) {
        await walk(fullPath, files);
      }

      continue;
    }

    files.push(fullPath);
  }

  return files;
}

function collectTagValues(xml: string, tagName: string): string[] {
  const regex = new RegExp(`<${tagName}>\\s*([\\s\\S]*?)\\s*<\\/${tagName}>`, "gi");
  const values: string[] = [];
  let match: RegExpExecArray | null;

  while ((match = regex.exec(xml)) !== null) {
    const raw = match[1]
      .split(";")
      .map((value) => value.trim())
      .filter(Boolean);
    values.push(...raw);
  }

  return values;
}

function firstTagValue(xml: string, tagName: string): string | null {
  const values = collectTagValues(xml, tagName);
  return values[0] ?? null;
}

function collectPackageReferences(xml: string): PackageReference[] {
  const packages: PackageReference[] = [];
  const regex = /<PackageReference\b([\s\S]*?)(?:\/>|>([\s\S]*?)<\/PackageReference>)/gi;
  let match: RegExpExecArray | null;

  while ((match = regex.exec(xml)) !== null) {
    const attributes = match[1] ?? "";
    const body = match[2] ?? "";
    const includeMatch = /(?:Include|Update)="([^"]+)"/i.exec(attributes);
    if (!includeMatch?.[1]) {
      continue;
    }

    const versionMatch = /Version="([^"]+)"/i.exec(attributes) ?? /<Version>\s*([^<]+)\s*<\/Version>/i.exec(body);
    packages.push({
      include: includeMatch[1].trim(),
      version: versionMatch?.[1]?.trim() || null,
    });
  }

  return packages;
}

function unique(values: string[]): string[] {
  return Array.from(new Set(values.filter(Boolean))).sort((left, right) => left.localeCompare(right));
}

function analyzeProject(repoPath: string, filePath: string, xml: string): ProjectInfo {
  const relativePath = path.relative(repoPath, filePath);
  const sdkMatch = /<Project\b[^>]*Sdk="([^"]+)"/i.exec(xml);
  const sdk = sdkMatch?.[1]?.trim() || null;
  const targetFrameworks = unique([
    ...collectTagValues(xml, "TargetFramework"),
    ...collectTagValues(xml, "TargetFrameworks"),
  ]);
  const nullable = firstTagValue(xml, "Nullable");
  const implicitUsings = firstTagValue(xml, "ImplicitUsings");
  const packages = collectPackageReferences(xml);

  const analyzerPackages = unique(
    packages
      .filter((pkg) => /analyzers?|roslynator|meziantou|stylecop/i.test(pkg.include))
      .map((pkg) => pkg.include),
  );
  const efCorePackages = unique(packages.filter((pkg) => /EntityFrameworkCore/i.test(pkg.include)).map((pkg) => pkg.include));
  const aspNetCorePackages = unique(
    packages.filter((pkg) => /AspNetCore/i.test(pkg.include)).map((pkg) => pkg.include),
  );
  if (sdk === "Microsoft.NET.Sdk.Web") {
    aspNetCorePackages.push("Microsoft.NET.Sdk.Web");
  }

  const testPackages = unique(
    packages
      .filter((pkg) => /(xunit|nunit|mstest|tunit|microsoft\.net\.test\.sdk)/i.test(pkg.include))
      .map((pkg) => pkg.include),
  );

  const isTestProject =
    /<IsTestProject>\s*true\s*<\/IsTestProject>/i.test(xml) ||
    testPackages.length > 0 ||
    /\.(tests?|test)\.csproj$/i.test(path.basename(filePath));

  return {
    path: relativePath,
    sdk,
    targetFrameworks,
    nullable,
    implicitUsings,
    isTestProject,
    aspNetCore: unique(aspNetCorePackages),
    efCore: efCorePackages,
    analyzers: analyzerPackages,
    testPackages,
  };
}

function analyzeEditorConfig(repoPath: string, filePath: string, text: string) {
  return {
    path: path.relative(repoPath, filePath),
    dotnetDiagnosticRules: (text.match(/dotnet_diagnostic\./gi) || []).length,
    mentionsNullable: /nullable/i.test(text),
  };
}

function isCiFile(relativePath: string): boolean {
  const normalized = relativePath.replace(/\\/g, "/");
  return (
    normalized.startsWith(".github/workflows/") ||
    normalized === "azure-pipelines.yml" ||
    normalized === "azure-pipelines.yaml" ||
    normalized === ".gitlab-ci.yml"
  );
}

async function run(): Promise<void> {
  const { repoPath } = parseArgs();
  const files = await walk(repoPath);

  const csprojFiles = files.filter((filePath) => filePath.toLowerCase().endsWith(".csproj"));
  const solutionFiles = files.filter((filePath) => filePath.toLowerCase().endsWith(".sln"));
  const editorConfigFiles = files.filter((filePath) => path.basename(filePath).toLowerCase() === ".editorconfig");
  const directoryBuildProps = files.filter((filePath) => path.basename(filePath) === "Directory.Build.props");
  const directoryBuildTargets = files.filter((filePath) => path.basename(filePath) === "Directory.Build.targets");
  const ciFiles = files
    .map((filePath) => path.relative(repoPath, filePath))
    .filter((relativePath) => isCiFile(relativePath))
    .sort((left, right) => left.localeCompare(right));

  const projects = [];
  for (const filePath of csprojFiles) {
    const xml = stripBom(await fs.readFile(filePath, "utf8"));
    projects.push(analyzeProject(repoPath, filePath, xml));
  }

  const editorConfigSignals = [];
  for (const filePath of editorConfigFiles) {
    const text = stripBom(await fs.readFile(filePath, "utf8"));
    editorConfigSignals.push(analyzeEditorConfig(repoPath, filePath, text));
  }

  const nullableSummary = {
    enabled: projects.filter((project) => /^enable$/i.test(project.nullable || "")).length,
    disabled: projects.filter((project) => /^disable$/i.test(project.nullable || "")).length,
    partial: projects.filter((project) => /^(annotations|warnings)$/i.test(project.nullable || "")).length,
    unspecified: projects.filter((project) => !project.nullable).length,
  };

  const summary = {
    totalProjects: projects.length,
    testProjects: projects.filter((project) => project.isTestProject).map((project) => project.path),
    targetFrameworks: unique(projects.flatMap((project) => project.targetFrameworks)),
    nullable: nullableSummary,
    analyzers: unique(projects.flatMap((project) => project.analyzers)),
    usesAspNetCore: projects.some((project) => project.aspNetCore.length > 0),
    usesEfCore: projects.some((project) => project.efCore.length > 0),
    testFrameworks: unique(projects.flatMap((project) => project.testPackages)),
    ciPresent: ciFiles.length > 0,
  };

  console.log(
    JSON.stringify(
      {
        repoPath,
        files: {
          solutions: solutionFiles.map((filePath) => path.relative(repoPath, filePath)).sort((left, right) => left.localeCompare(right)),
          directoryBuildProps: directoryBuildProps
            .map((filePath) => path.relative(repoPath, filePath))
            .sort((left, right) => left.localeCompare(right)),
          directoryBuildTargets: directoryBuildTargets
            .map((filePath) => path.relative(repoPath, filePath))
            .sort((left, right) => left.localeCompare(right)),
          editorConfigs: editorConfigSignals,
          ciFiles,
        },
        summary,
        projects: projects.sort((left, right) => left.path.localeCompare(right.path)),
      },
      null,
      2,
    ),
  );
}

async function main(): Promise<void> {
  const { selfCheck } = parseArgs();
  if (selfCheck) {
    const sample = `
      <Project Sdk="Microsoft.NET.Sdk.Web">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
          <Nullable>enable</Nullable>
          <ImplicitUsings>enable</ImplicitUsings>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
          <PackageReference Include="StyleCop.Analyzers">
            <Version>1.2.0</Version>
          </PackageReference>
          <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
        </ItemGroup>
      </Project>
    `;
    const project = analyzeProject("/repo", "/repo/tests/Sample.Tests.csproj", sample);
    assert.deepEqual(project.targetFrameworks, ["net10.0"]);
    assert.equal(project.nullable, "enable");
    assert.equal(project.isTestProject, true);
    assert.deepEqual(project.analyzers, ["StyleCop.Analyzers"]);
    assert.deepEqual(project.efCore, ["Microsoft.EntityFrameworkCore"]);
    assert.equal(project.aspNetCore.includes("Microsoft.NET.Sdk.Web"), true);
    console.log("inspect_dotnet_repo self-check ok");
    return;
  }

  await run();
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(message);
  process.exitCode = 1;
});
