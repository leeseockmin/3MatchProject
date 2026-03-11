[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$TargetSkillsRoot = Join-Path $RepoRoot ".agent-skills"
$TargetAgentsRoot = Join-Path $RepoRoot ".agent-agents"
$StateRoot = Join-Path $RepoRoot ".omc\state"
$ManifestPath = Join-Path $StateRoot "skills-install-manifest.json"

$sources = @(
    @{
        Name = "agents-skills"
        Root = "C:\Users\user\.agents\skills"
        Entries = @(
            @{ Source = "api-design"; Target = "api-design" },
            @{ Source = "api-documentation"; Target = "api-documentation" },
            @{ Source = "backend-testing"; Target = "backend-testing" },
            @{ Source = "bmad-gds"; Target = "bmad-gds" },
            @{ Source = "bmad-idea"; Target = "bmad-idea" },
            @{ Source = "changelog-maintenance"; Target = "changelog-maintenance" },
            @{ Source = "code-refactoring"; Target = "code-refactoring" },
            @{ Source = "code-review"; Target = "code-review" },
            @{ Source = "codebase-search"; Target = "codebase-search" },
            @{ Source = "data-analysis"; Target = "data-analysis" },
            @{ Source = "database-schema-design"; Target = "database-schema-design" },
            @{ Source = "design-system"; Target = "design-system" },
            @{ Source = "environment-setup"; Target = "environment-setup" },
            @{ Source = "file-organization"; Target = "file-organization" },
            @{ Source = "git-submodule"; Target = "git-submodule" },
            @{ Source = "git-workflow"; Target = "git-workflow" },
            @{ Source = "image-generation"; Target = "image-generation" },
            @{ Source = "jeo"; Target = "jeo" },
            @{ Source = "llm-monitoring-dashboard"; Target = "llm-monitoring-dashboard" },
            @{ Source = "log-analysis"; Target = "log-analysis" },
            @{ Source = "marketing-skills-collection"; Target = "marketing-skills-collection" },
            @{ Source = "ohmg"; Target = "ohmg" },
            @{ Source = "omc"; Target = "omc" },
            @{ Source = "omx"; Target = "omx" },
            @{ Source = "opencontext"; Target = "opencontext" },
            @{ Source = "pattern-detection"; Target = "pattern-detection" },
            @{ Source = "performance-optimization"; Target = "performance-optimization" },
            @{ Source = "plannotator"; Target = "plannotator" },
            @{ Source = "pptx-presentation-builder"; Target = "pptx-presentation-builder" },
            @{ Source = "prompt-repetition"; Target = "prompt-repetition" },
            @{ Source = "ralph"; Target = "ralph" },
            @{ Source = "ralphmode"; Target = "ralphmode" },
            @{ Source = "remotion-video-production"; Target = "remotion-video-production" },
            @{ Source = "security-best-practices"; Target = "security-best-practices" },
            @{ Source = "task-estimation"; Target = "task-estimation" },
            @{ Source = "task-planning"; Target = "task-planning" },
            @{ Source = "testing-strategies"; Target = "testing-strategies" },
            @{ Source = "ui-component-patterns"; Target = "ui-component-patterns" },
            @{ Source = "unity-mcp"; Target = "unity-mcp" },
            @{ Source = "vibe-kanban"; Target = "vibe-kanban" },
            @{ Source = "video-production"; Target = "video-production" },
            @{ Source = "web-accessibility"; Target = "web-accessibility" },
            @{ Source = "web-design-guidelines"; Target = "web-design-guidelines" },
            @{ Source = "workflow-automation"; Target = "workflow-automation" }
        )
    },
    @{
        Name = "codex-system-skills"
        Root = "C:\Users\user\.codex\skills\.system"
        Entries = @(
            @{ Source = "skill-creator"; Target = "skill-creator" },
            @{ Source = "skill-installer"; Target = "skill-installer" }
        )
    },
    @{
        Name = "codex-skills"
        Root = "C:\Users\user\.codex\skills"
        Entries = @(
            @{ Source = "unity-mcp-skill"; Target = "unity-mcp-skill" },
            @{ Source = "unity-mcp-skill"; Target = "unity-mcp-orchestrator" }
        )
    }
)

New-Item -ItemType Directory -Force -Path $TargetSkillsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $TargetAgentsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $StateRoot | Out-Null

function Copy-SkillDirectory {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    if (Test-Path $TargetPath) {
        Remove-Item -Recurse -Force $TargetPath
    }

    Copy-Item -Recurse -Force $SourcePath $TargetPath
}

$installedSkills = New-Object System.Collections.Generic.List[string]
$mirroredAgents = New-Object System.Collections.Generic.List[string]
$missingEntries = New-Object System.Collections.Generic.List[string]

foreach ($source in $sources) {
    foreach ($entry in $source.Entries) {
        $sourcePath = Join-Path $source.Root $entry.Source
        $targetPath = Join-Path $TargetSkillsRoot $entry.Target

        if (-not (Test-Path $sourcePath)) {
            $missingEntries.Add("$($source.Name):$($entry.Source)")
            continue
        }

        Copy-SkillDirectory -SourcePath $sourcePath -TargetPath $targetPath
        $installedSkills.Add($entry.Target)

        $agentSource = Join-Path $sourcePath "agents"
        if (Test-Path $agentSource) {
            $agentTarget = Join-Path $TargetAgentsRoot $entry.Target
            Copy-SkillDirectory -SourcePath $agentSource -TargetPath $agentTarget
            $mirroredAgents.Add($entry.Target)
        }
    }
}

$manifest = [ordered]@{
    installed_at = (Get-Date).ToString("o")
    workspace = $RepoRoot
    installed_skill_count = $installedSkills.Count
    installed_skills = @($installedSkills | Sort-Object -Unique)
    mirrored_agent_count = $mirroredAgents.Count
    mirrored_agents = @($mirroredAgents | Sort-Object -Unique)
    missing_entries = @($missingEntries)
    blocked_external_installs = @(
        "setup-all-skills-prompt.md could not be fetched because outbound network access is blocked",
        "bash-based JEO scripts cannot run because bash is not installed on this machine"
    )
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $ManifestPath -Encoding utf8

Write-Output "Installed $($installedSkills.Count) skill entries into $TargetSkillsRoot"
Write-Output "Mirrored $($mirroredAgents.Count) agent asset directories into $TargetAgentsRoot"
Write-Output "Wrote manifest: $ManifestPath"
