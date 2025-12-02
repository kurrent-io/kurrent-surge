# Build Configuration

## Overview

Proper build configuration ensures resource files are correctly embedded, satellite assemblies are generated, and Designer.cs files are maintained. This guide covers MSBuild, .csproj configuration, and CI/CD pipeline setup.

## .csproj Configuration

### Basic Resource Configuration

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Exe</OutputType>

    <!-- Specify neutral language (default culture) -->
    <NeutralLanguage>en-US</NeutralLanguage>
  </PropertyGroup>

  <ItemGroup>
    <!-- EmbeddedResource automatically includes *.resx files -->
    <EmbeddedResource Update="Resources\Resources.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <LastGenOutput>Resources.Designer.cs</LastGenOutput>
    </EmbeddedResource>

    <!-- Corresponding Designer.cs file -->
    <Compile Update="Resources\Resources.Designer.cs">
      <DesignTime>True</DesignTime>
      <AutoGen>True</AutoGen>
      <DependentUpon>Resources.resx</DependentUpon>
    </Compile>
  </ItemGroup>
</Project>
```

### Satellite Assembly Configuration

```xml
<PropertyGroup>
  <!-- Explicitly define cultures for satellite assemblies -->
  <SatelliteCultures>es;es-MX;fr;fr-CA;de;ja;zh-CN</SatelliteCultures>

  <!-- Location of neutral culture resources -->
  <NeutralResourcesLanguageAttribute>en-US</NeutralResourcesLanguageAttribute>
</PropertyGroup>
```

### Public Resources (Accessible from Other Assemblies)

```xml
<ItemGroup>
  <EmbeddedResource Update="Resources\SharedResources.resx">
    <!-- Use PublicResXFileCodeGenerator for public access -->
    <Generator>PublicResXFileCodeGenerator</Generator>
    <LastGenOutput>SharedResources.Designer.cs</LastGenOutput>
  </EmbeddedResource>
</ItemGroup>
```

### Custom Resource Path

```xml
<PropertyGroup>
  <!-- Change default resource location -->
  <ResourcesPath>Localization</ResourcesPath>
</PropertyGroup>
```

## MSBuild Targets

### Custom Resource Processing Target

```xml
<Target Name="ValidateResources" BeforeTargets="PrepareResources">
  <Message Text="Validating resource files..." Importance="high" />

  <!-- Find all .resx files -->
  <ItemGroup>
    <ResxFiles Include="**\*.resx" />
  </ItemGroup>

  <!-- Custom validation logic -->
  <Exec Command="powershell -File $(ProjectDir)Scripts\Validate-Resources.ps1 -Path %(ResxFiles.Identity)"
        ContinueOnError="false" />

  <Message Text="Resource validation complete." Importance="high" />
</Target>
```

### Generate Missing Culture Files

```xml
<Target Name="GenerateMissingCultures" BeforeTargets="PrepareResources">
  <PropertyGroup>
    <RequiredCultures>es;es-MX;fr;fr-CA;de</RequiredCultures>
  </PropertyGroup>

  <Exec Command="powershell -File $(ProjectDir)Scripts\Generate-Missing-Cultures.ps1 -Cultures $(RequiredCultures)"
        ContinueOnError="true" />
</Target>
```

### Post-Build Resource Validation

```xml
<Target Name="ValidateSatelliteAssemblies" AfterTargets="Build">
  <Message Text="Validating satellite assemblies..." Importance="high" />

  <!-- Check for expected satellite assemblies -->
  <ItemGroup>
    <ExpectedSatellites Include="$(OutputPath)es\$(AssemblyName).resources.dll" />
    <ExpectedSatellites Include="$(OutputPath)fr\$(AssemblyName).resources.dll" />
  </ItemGroup>

  <Error Condition="!Exists('%(ExpectedSatellites.Identity)')"
         Text="Missing satellite assembly: %(ExpectedSatellites.Identity)" />

  <Message Text="All satellite assemblies present." Importance="high" />
</Target>
```

## Generator Options

### ResXFileCodeGenerator (Internal Class)

```xml
<EmbeddedResource Update="Resources.resx">
  <Generator>ResXFileCodeGenerator</Generator>
  <LastGenOutput>Resources.Designer.cs</LastGenOutput>
</EmbeddedResource>
```

**Generated Code:**
```csharp
internal class Resources
{
    // ... internal access
}
```

### PublicResXFileCodeGenerator (Public Class)

```xml
<EmbeddedResource Update="SharedResources.resx">
  <Generator>PublicResXFileCodeGenerator</Generator>
  <LastGenOutput>SharedResources.Designer.cs</LastGenOutput>
</EmbeddedResource>
```

**Generated Code:**
```csharp
public class SharedResources
{
    // ... public access
}
```

## CI/CD Pipeline Configuration

### GitHub Actions

```yaml
name: Build and Test

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Validate Resources
      run: |
        dotnet test --filter "Category=ResourceValidation" --no-build --configuration Release

    - name: Check Satellite Assemblies
      run: |
        for culture in es es-MX fr fr-CA de; do
          if [ ! -f "src/MyApp/bin/Release/net9.0/$culture/MyApp.resources.dll" ]; then
            echo "Missing satellite assembly for culture: $culture"
            exit 1
          fi
        done

    - name: Test
      run: dotnet test --no-build --configuration Release --verbosity normal

    - name: Publish
      run: dotnet publish --no-build --configuration Release --output ./publish

    - name: Upload Artifacts
      uses: actions/upload-artifact@v3
      with:
        name: published-app
        path: ./publish
```

### Azure DevOps

```yaml
trigger:
  branches:
    include:
      - main
      - develop

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'

steps:
- task: UseDotNet@2
  displayName: 'Install .NET SDK'
  inputs:
    version: '9.0.x'

- task: DotNetCoreCLI@2
  displayName: 'Restore NuGet packages'
  inputs:
    command: 'restore'

- task: DotNetCoreCLI@2
  displayName: 'Build solution'
  inputs:
    command: 'build'
    arguments: '--configuration $(buildConfiguration) --no-restore'

- task: PowerShell@2
  displayName: 'Validate Resources'
  inputs:
    targetType: 'filePath'
    filePath: 'Scripts/Validate-All-Resources.ps1'

- task: DotNetCoreCLI@2
  displayName: 'Run Tests'
  inputs:
    command: 'test'
    arguments: '--configuration $(buildConfiguration) --no-build'

- task: DotNetCoreCLI@2
  displayName: 'Publish'
  inputs:
    command: 'publish'
    publishWebProjects: false
    projects: '**/*.csproj'
    arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)'

- task: PublishBuildArtifacts@1
  displayName: 'Publish Artifacts'
  inputs:
    pathToPublish: '$(Build.ArtifactStagingDirectory)'
    artifactName: 'drop'
```

## NuGet Package Configuration

### .nuspec for Localized Package

```xml
<?xml version="1.0"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>MyApp.Resources</id>
    <version>1.0.0</version>
    <authors>Your Company</authors>
    <description>Localized resources for MyApp</description>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
  </metadata>

  <files>
    <!-- Main assembly -->
    <file src="bin\Release\net9.0\MyApp.dll" target="lib\net9.0" />

    <!-- Default resources (embedded in main assembly) -->

    <!-- Satellite assemblies -->
    <file src="bin\Release\net9.0\es\MyApp.resources.dll" target="lib\net9.0\es" />
    <file src="bin\Release\net9.0\es-MX\MyApp.resources.dll" target="lib\net9.0\es-MX" />
    <file src="bin\Release\net9.0\fr\MyApp.resources.dll" target="lib\net9.0\fr" />
    <file src="bin\Release\net9.0\fr-CA\MyApp.resources.dll" target="lib\net9.0\fr-CA" />
    <file src="bin\Release\net9.0\de\MyApp.resources.dll" target="lib\net9.0\de" />
  </files>
</package>
```

### .csproj with NuGet Package Properties

```xml
<PropertyGroup>
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  <PackageId>MyApp.Resources</PackageId>
  <Version>1.0.0</Version>
  <Authors>Your Company</Authors>
  <Description>Localized resources for MyApp</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>

  <!-- Include satellite assemblies in package -->
  <IncludeSatelliteOutputInPack>true</IncludeSatelliteOutputInPack>
</PropertyGroup>
```

## Troubleshooting Build Issues

### Issue: Designer.cs Not Regenerating

**Solution:**
```xml
<!-- Force regeneration by setting custom tool -->
<EmbeddedResource Update="Resources.resx">
  <Generator>ResXFileCodeGenerator</Generator>
  <LastGenOutput>Resources.Designer.cs</LastGenOutput>
  <CustomToolNamespace>MyApp.Properties</CustomToolNamespace>
</EmbeddedResource>
```

**Or use MSBuild:**
```bash
# Clean and rebuild
dotnet clean
dotnet build
```

### Issue: Satellite Assemblies Not Generated

**Cause:** Culture-specific .resx files not properly configured

**Solution:**
```xml
<!-- Ensure culture files are included -->
<ItemGroup>
  <EmbeddedResource Include="Resources\Resources.es.resx" />
  <EmbeddedResource Include="Resources\Resources.fr.resx" />
</ItemGroup>

<!-- Or use wildcard -->
<ItemGroup>
  <EmbeddedResource Include="Resources\*.resx" />
</ItemGroup>
```

### Issue: Resources Not Embedded in Assembly

**Solution:**
```xml
<!-- Explicitly set as EmbeddedResource -->
<ItemGroup>
  <EmbeddedResource Include="Resources\Resources.resx">
    <LogicalName>MyApp.Properties.Resources.resources</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

### Issue: Wrong Namespace in Generated Code

**Solution:**
```xml
<EmbeddedResource Update="Resources.resx">
  <Generator>ResXFileCodeGenerator</Generator>
  <CustomToolNamespace>MyApp.Resources</CustomToolNamespace>
</EmbeddedResource>
```

## Build Optimization

### Incremental Build Support

```xml
<PropertyGroup>
  <!-- Enable incremental build for resources -->
  <GenerateResourceNeverLockTypeAssemblies>true</GenerateResourceNeverLockTypeAssemblies>
</PropertyGroup>
```

### Parallel Build

```bash
# Enable parallel project builds
dotnet build /m

# Specify max CPU count
dotnet build /m:4
```

### Resource Cache

```xml
<PropertyGroup>
  <!-- Use precompiled resources for faster builds -->
  <GenerateResourceUsePreserializedResources>true</GenerateResourceUsePreserializedResources>
</PropertyGroup>
```

## Best Practices

### 1. Version Satellite Assemblies with Main Assembly

```xml
<PropertyGroup>
  <!-- Ensure consistent versioning -->
  <Version>1.2.3</Version>
  <AssemblyVersion>1.2.3.0</AssemblyVersion>
  <FileVersion>1.2.3.0</FileVersion>
</PropertyGroup>
```

### 2. Validate Resources in CI

```xml
<Target Name="CI_ValidateResources" AfterTargets="Build" Condition="'$(CI)' == 'true'">
  <Exec Command="dotnet test --filter Category=ResourceValidation --no-build" />
</Target>
```

### 3. Fail Build on Missing Translations

```xml
<Target Name="CheckTranslationCompleteness" AfterTargets="PrepareResources">
  <Exec Command="powershell -File $(ProjectDir)Scripts\Check-Completeness.ps1"
        ContinueOnError="false"
        IgnoreExitCode="false" />
</Target>
```

### 4. Generate Resource Report

```xml
<Target Name="GenerateResourceReport" AfterTargets="Build">
  <Exec Command="powershell -File $(ProjectDir)Scripts\Generate-Resource-Report.ps1 -OutputPath $(OutputPath)resource-report.html" />
</Target>
```

## Summary

- **.csproj**: Configure EmbeddedResource, Generator, SatelliteCultures
- **MSBuild Targets**: Custom validation, missing culture generation, post-build checks
- **Generators**: ResXFileCodeGenerator (internal), PublicResXFileCodeGenerator (public)
- **CI/CD**: Automate build, test, validation in pipelines
- **NuGet**: Include satellite assemblies in packages
- **Troubleshooting**: Designer.cs regeneration, satellite assembly generation, embedding
- **Best Practices**: Versioning, CI validation, build optimization

Proper build configuration ensures consistent, reliable resource compilation and deployment across all environments.
