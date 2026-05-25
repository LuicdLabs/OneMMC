# ManagementTools Localization Implementation Guide

## Table of Contents
1. [Overview](#overview)
2. [Architecture Design](#architecture-design)
3. [File Organization](#file-organization)
4. [Implementing Localization Support for a New Feature](#implementing-localization-support-for-a-new-feature)
5. [Using Localized Strings in XAML](#using-localized-strings-in-xaml)
6. [Using Localized Strings in C#](#using-localized-strings-in-c)
7. [Using Localized Strings in Converters](#using-localized-strings-in-converters)
8. [Adding Support for a New Language](#adding-support-for-a-new-language)
9. [Best Practices](#best-practices)
10. [FAQ](#faq)

---

## Overview

ManagementTools implements localization support by using the **WinUI 3 resource system (.resw)**. This project uses a modular design: localized strings for different features are split into separate resource files, making them easier to maintain and extend.

### Supported Languages
- **English (en-US)** - Default language
- **Traditional Chinese (zh-TW)** - Fully supported

### Core Components
- **LocalizedStrings** - Unified access point for localized strings (Partial Class)
- **LocalizationService** - Resource loading and management service
- **ResourceFiles** - Constant definitions for resource file names
- **.resw files** - XML-based resource files

---

## Architecture Design

### 1. Layered Architecture

```
┌─────────────────────────────────────┐
│         UI Layer (XAML/C#)          │
│  Access strings through             │
│  LocalizedStrings                   │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│      LocalizedStrings (Partial)     │
│  - LocalizedStrings.cs (base class) │
│  - LocalizedStrings.Common.cs       │
│  - LocalizedStrings.Services.cs     │
│  - LocalizedStrings.ComExp.cs       │
│  - ... (feature-based partials)     │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│       LocalizationService           │
│  Loads resources from .resw files   │
└──────────────┬──────────────────────┘
               │
┌──────────────▼──────────────────────┐
│      Resource Files (.resw)         │
│  - Strings/en-US/*.resw             │
│  - Strings/zh-TW/*.resw             │
└─────────────────────────────────────┘
```

### 2. Partial Class Design

`LocalizedStrings` uses a partial class design, splitting strings for different features across different files:

```csharp
// LocalizedStrings.cs - Base class
public partial class LocalizedStrings
{
    protected static string GetResource(string resourceFile, string key) { }
}

// LocalizedStrings.Common.cs - Common strings
public partial class LocalizedStrings
{
    public string Common_OKButton => GetResource(ResourceFiles.Common, "Common_OKButton");
}

// LocalizedStrings.Services.cs - Service management strings
public partial class LocalizedStrings
{
    public string Services_Start => GetResource(ResourceFiles.Services, "Services_Start");
}
```

---

## File Organization

```
ManagementTools/
├── Localization/                          # Localization code
│   ├── LocalizedStrings.cs                # Base class
│   ├── LocalizedStrings.Common.cs         # Common strings
│   ├── LocalizedStrings.Navigation.cs     # Navigation strings
│   ├── LocalizedStrings.Services.cs       # Service management
│   ├── LocalizedStrings.ComExp.cs         # Component Services
│   ├── LocalizedStrings.DiskManagement.cs # Disk Management
│   ├── LocalizedStrings.*.cs              # Other features...
│   ├── LocalizationService.cs             # Resource loading service
│   ├── ResourceFiles.cs                   # Resource file constants
│   └── UILocalizationProvider.cs          # UI-layer localization provider
│
├── Strings/                               # Resource files
│   ├── en-US/                             # English resources
│   │   ├── Resources.resw                 # Application-level resources
│   │   ├── Common.resw                    # Common strings
│   │   ├── Navigation.resw                # Navigation
│   │   ├── Services.resw                  # Service management
│   │   ├── ComExp.resw                    # Component Services
│   │   └── *.resw                         # Other features...
│   │
│   └── zh-TW/                             # Traditional Chinese resources
│       ├── Resources.resw
│       ├── Common.resw
│       ├── Navigation.resw
│       ├── Services.resw
│       ├── ComExp.resw
│       └── *.resw
│
└── Converters/                            # Value converters
    ├── PropertyConverters.cs              # Includes BoolToYesNoConverter, etc.
    └── *.cs
```

---

## Implementing Localization Support for a New Feature

Assume we want to add localization support for a new feature named `NetworkManager`.

### Step 1: Create Resource Files

#### 1.1 Create the English Resource File
**File**: `ManagementTools/Strings/en-US/NetworkManager.resw`

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <!-- Schema definitions... (copy from another .resw file) -->
  
  <data name="NetworkManager_PageTitle" xml:space="preserve">
    <value>Network Manager</value>
  </data>
  <data name="NetworkManager_Adapters" xml:space="preserve">
    <value>Network Adapters</value>
  </data>
  <data name="NetworkManager_Status" xml:space="preserve">
    <value>Status</value>
  </data>
  <data name="NetworkManager_Enable" xml:space="preserve">
    <value>Enable</value>
  </data>
  <data name="NetworkManager_Disable" xml:space="preserve">
    <value>Disable</value>
  </data>
</root>
```

#### 1.2 Create the Traditional Chinese Resource File
**File**: `ManagementTools/Strings/zh-TW/NetworkManager.resw`

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <!-- Schema definitions... -->
  
  <data name="NetworkManager_PageTitle" xml:space="preserve">
    <value>網路管理員</value>
  </data>
  <data name="NetworkManager_Adapters" xml:space="preserve">
    <value>網路介面卡</value>
  </data>
  <data name="NetworkManager_Status" xml:space="preserve">
    <value>狀態</value>
  </data>
  <data name="NetworkManager_Enable" xml:space="preserve">
    <value>啟用</value>
  </data>
  <data name="NetworkManager_Disable" xml:space="preserve">
    <value>停用</value>
  </data>
</root>
```

### Step 2: Update ResourceFiles.cs

**File**: `ManagementTools/Localization/ResourceFiles.cs`

```csharp
public static class ResourceFiles
{
    // ... existing constants ...
    
    /// <summary>Network Manager strings (NetworkManager.resw)</summary>
    public const string NetworkManager = "NetworkManager";
}
```

### Step 3: Create a LocalizedStrings Partial Class

**File**: `ManagementTools/Localization/LocalizedStrings.NetworkManager.cs`

```csharp
namespace ManagementTools.Localization
{
    /// <summary>
    /// Localized strings for Network Manager feature.
    /// Resources are loaded from NetworkManager.resw file.
    /// </summary>
    public partial class LocalizedStrings
    {
        // Page Title
        public string NetworkManager_PageTitle => 
            GetResource(ResourceFiles.NetworkManager, "NetworkManager_PageTitle");
        
        // UI Elements
        public string NetworkManager_Adapters => 
            GetResource(ResourceFiles.NetworkManager, "NetworkManager_Adapters");
        
        public string NetworkManager_Status => 
            GetResource(ResourceFiles.NetworkManager, "NetworkManager_Status");
        
        // Actions
        public string NetworkManager_Enable => 
            GetResource(ResourceFiles.NetworkManager, "NetworkManager_Enable");
        
        public string NetworkManager_Disable => 
            GetResource(ResourceFiles.NetworkManager, "NetworkManager_Disable");
    }
}
```

### Step 4: Use the Strings in XAML

**File**: `ManagementTools/Views/NetworkManagerPage.xaml`

```xml
<Page
    x:Class="ManagementTools.Views.NetworkManagerPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ManagementTools.Localization">

    <Page.Resources>
        <local:LocalizedStrings x:Key="LocalizedStrings" />
    </Page.Resources>

    <Grid>
        <!-- Use localized strings -->
        <TextBlock 
            Text="{Binding Source={StaticResource LocalizedStrings}, Path=NetworkManager_PageTitle}" 
            Style="{StaticResource TitleTextBlockStyle}" />
        
        <Button 
            Content="{Binding Source={StaticResource LocalizedStrings}, Path=NetworkManager_Enable}" 
            Command="{x:Bind ViewModel.EnableCommand}" />
    </Grid>
</Page>
```

---

## Using Localized Strings in XAML

### Method 1: Use StaticResource (Recommended)

```xml
<Page.Resources>
    <local:LocalizedStrings x:Key="LocalizedStrings" />
</Page.Resources>

<!-- Simple text binding -->
<TextBlock Text="{Binding Source={StaticResource LocalizedStrings}, Path=Common_OKButton}" />

<!-- Button content -->
<Button Content="{Binding Source={StaticResource LocalizedStrings}, Path=Services_Start}" />

<!-- CommandBar label -->
<AppBarButton 
    Icon="Refresh" 
    Label="{Binding Source={StaticResource LocalizedStrings}, Path=Common_Refresh}" />
```

### Method 2: Use x:Bind (Requires a Code-Behind Property)

```xml
<!-- Code-behind must define a LocalizedStrings property -->
<TextBlock Text="{x:Bind LocalizedStrings.Common_OKButton}" />
```

```csharp
// Code-behind
public sealed partial class MyPage : Page
{
    public LocalizedStrings LocalizedStrings { get; } = new();
    
    public MyPage()
    {
        InitializeComponent();
    }
}
```

### Method 3: Formatted Strings

For strings that require dynamic values, such as `"Current PC: {0} (local)"`:

```xml
<!-- XAML -->
<TextBlock Text="{x:Bind CurrentPCText, Mode=OneWay}" />
```

```csharp
// Code-behind
public string CurrentPCText => 
    string.Format(LocalizedStrings.ComExp_CurrentPC, Environment.MachineName);
```

---

## Using Localized Strings in C#

### Basic Usage

```csharp
using ManagementTools.Localization;

public class MyViewModel
{
    private readonly LocalizedStrings _localizedStrings = new();
    
    public void ShowMessage()
    {
        string title = _localizedStrings.Common_SuccessTitle;
        string message = _localizedStrings.Common_LoadedSuccessfully;
        
        // Show message...
    }
}
```

### Formatted Strings

```csharp
// Resource file: "Loaded {0} items."
string message = string.Format(
    _localizedStrings.ComExp_LoadedCount, 
    itemCount
);

// Or use GetFormattedString
string message = LocalizationService.Instance.GetFormattedString(
    ResourceFiles.ComExp, 
    "ComExp_LoadedCount", 
    itemCount
);
```

### Usage in a ViewModel

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using ManagementTools.Localization;

public partial class NetworkManagerViewModel : ObservableObject
{
    private readonly LocalizedStrings _localizedStrings = new();
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    public async Task LoadDataAsync()
    {
        StatusMessage = _localizedStrings.Common_LoadingData;
        
        try
        {
            // Load data...
            StatusMessage = _localizedStrings.Common_LoadedSuccessfully;
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(
                _localizedStrings.Common_OperationFailed, 
                ex.Message
            );
        }
    }
}
```

---

## Using Localized Strings in Converters

### Example: BoolToYesNoConverter

```csharp
using Microsoft.UI.Xaml.Data;
using System;
using ManagementTools.Localization;

namespace ManagementTools.Converters
{
    public class BoolToYesNoConverter : IValueConverter
    {
        private static readonly LocalizedStrings _localizedStrings = new();

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue 
                    ? _localizedStrings.ComExp_Yes 
                    : _localizedStrings.ComExp_No;
            }
            return _localizedStrings.ComExp_No;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
```

### Use the Converter in XAML

```xml
<Page.Resources>
    <converters:BoolToYesNoConverter x:Key="BoolToYesNoConverter" />
</Page.Resources>

<TextBlock Text="{Binding IsEnabled, Converter={StaticResource BoolToYesNoConverter}}" />
```

---

## Adding Support for a New Language

Assume we want to add support for Simplified Chinese (`zh-CN`):

### Step 1: Create the Language Folder

Create a `zh-CN` folder under `ManagementTools/Strings/`.

### Step 2: Copy and Translate Resource Files

```
ManagementTools/Strings/zh-CN/
├── Resources.resw
├── Common.resw
├── Navigation.resw
├── Services.resw
├── ComExp.resw
└── ... (all other .resw files)
```

### Step 3: Translate the Strings

**Example**: `zh-CN/Common.resw`

```xml
<data name="Common_OKButton" xml:space="preserve">
  <value>确定</value>
</data>
<data name="Common_CancelButton" xml:space="preserve">
  <value>取消</value>
</data>
```

### Step 4: Test

1. Change the display language to Simplified Chinese in Windows Settings.
2. Restart the application.
3. Verify that all UI elements are displayed in Simplified Chinese.

---

## Best Practices

### 1. Naming Conventions

#### Resource Key Naming
```
[FeatureName]_[ElementType]_[SpecificDescription]

Examples:
- Services_Start              (action)
- Services_Status             (property)
- Services_SearchPlaceholder  (placeholder text)
- ComExp_Stats_Open           (statistics)
```

#### Partial Class File Naming
```
LocalizedStrings.[FeatureName].cs

Examples:
- LocalizedStrings.Services.cs
- LocalizedStrings.ComExp.cs
- LocalizedStrings.DiskManagement.cs
```

### 2. Organization Principles

#### Group by Feature
- Each major feature has its own `.resw` file.
- Common strings go in `Common.resw`.
- Navigation-related strings go in `Navigation.resw`.

#### Avoid Duplication
- Prefer common strings from `Common.resw`.
- Only define feature-specific strings in the feature-specific `.resw` file.

### 3. Formatted Strings

#### Use Placeholders
```xml
<!-- Resource file -->
<data name="Message_ItemsLoaded" xml:space="preserve">
  <value>Loaded {0} items in {1} seconds.</value>
</data>
```

```csharp
// C# usage
string message = string.Format(
    _localizedStrings.Message_ItemsLoaded, 
    itemCount, 
    elapsedSeconds
);
```

### 4. Handling Plurals

```xml
<!-- English -->
<data name="Common_CountItem_Singular" xml:space="preserve">
  <value>{0} item</value>
</data>
<data name="Common_CountItem_Plural" xml:space="preserve">
  <value>{0} items</value>
</data>
```

```csharp
// C# usage
string message = count == 1 
    ? string.Format(_localizedStrings.Common_CountItem_Singular, count)
    : string.Format(_localizedStrings.Common_CountItem_Plural, count);
```

### 5. Comments and Documentation

```csharp
/// <summary>
/// Localized strings for Network Manager feature.
/// Resources are loaded from NetworkManager.resw file.
/// </summary>
public partial class LocalizedStrings
{
    // Page Title
    public string NetworkManager_PageTitle => 
        GetResource(ResourceFiles.NetworkManager, "NetworkManager_PageTitle");
    
    // Actions - action-related strings
    public string NetworkManager_Enable => 
        GetResource(ResourceFiles.NetworkManager, "NetworkManager_Enable");
}
```

### 6. Performance Considerations

#### Use Static Instances (Converters)
```csharp
// Good practice - static instance to avoid repeated allocation
private static readonly LocalizedStrings _localizedStrings = new();

// Avoid - creating a new instance every time
public object Convert(...)
{
    var localizedStrings = new LocalizedStrings(); // ❌ Not recommended
}
```

#### Cache Formatted Results
```csharp
// If the same formatted string is reused multiple times
private string? _cachedMessage;

public string GetMessage(int count)
{
    if (_cachedMessage == null)
    {
        _cachedMessage = string.Format(_localizedStrings.Message_Template, count);
    }
    return _cachedMessage;
}
```

---

## FAQ

### Q1: Why is my localized string not displayed?

**Possible causes**:
1. The `.resw` file has an incorrect Build Action.
   - Fix: Make sure the Build Action is set to `PRIResource`.
2. The resource key name is misspelled.
   - Fix: Check that the `name` attribute in the `.resw` file matches the key name used in C#.
3. The `ResourceFiles` constant is not defined.
   - Fix: Add the corresponding constant in `ResourceFiles.cs`.

### Q2: How do I test different languages?

**Method 1: Change the Windows display language**
1. Settings → Time & language → Language & region
2. Add a language and set it as the display language.
3. Restart the application.

**Method 2: Use pseudo-localization**
```csharp
// Force a specific language in development builds
#if DEBUG
    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-TW";
#endif
```

### Q3: Can the placeholder order in formatted strings be changed?

Yes. Different languages may use different word orders:

```xml
<!-- English -->
<value>User {0} logged in at {1}</value>

<!-- Chinese -->
<value>{1} 時使用者 {0} 登入</value>
```

### Q4: How should long text be handled?

```xml
<!-- Use xml:space="preserve" to preserve formatting -->
<data name="Help_LongDescription" xml:space="preserve">
  <value>This is a very long description that spans
multiple lines. The formatting will be preserved
as written in the resource file.</value>
</data>
```

### Q5: Can HTML or special characters be used in resource files?

Yes, but they must be escaped correctly:

```xml
<!-- Special characters -->
<value>Price: &lt; $100</value>  <!-- < -->
<value>A &amp; B</value>          <!-- & -->
<value>Quote: &quot;Hello&quot;</value>  <!-- " -->

<!-- Newline -->
<value>Line 1&#x0A;Line 2</value>
```

### Q6: How should dynamic content such as a user name be handled?

Use formatted strings:

```xml
<data name="Welcome_Message" xml:space="preserve">
  <value>Welcome, {0}!</value>
</data>
```

```csharp
string message = string.Format(
    _localizedStrings.Welcome_Message, 
    userName
);
```

### Q7: Do localized strings in converters update automatically?

No, they do not update automatically. If runtime language switching is required, you need to:
1. Implement a language switching mechanism.
2. Notify all components that use localized strings to reload.
3. Consider using the `INotifyPropertyChanged` pattern.

### Q8: How should localized images or icons be handled?

```xml
<!-- Define the image path in the resource file -->
<data name="Image_Logo" xml:space="preserve">
  <value>ms-appx:///Assets/Logo_en.png</value>
</data>
```

```xml
<!-- Use it in XAML -->
<Image Source="{Binding Source={StaticResource LocalizedStrings}, Path=Image_Logo}" />
```

---

## Checklist

Before submitting code, make sure that:

- [ ] All hard-coded strings have been replaced with localized resources.
- [ ] Corresponding `.resw` files have been created for all supported languages.
- [ ] New resource file constants have been added to `ResourceFiles.cs`.
- [ ] The corresponding `LocalizedStrings` partial class has been created.
- [ ] All resource keys follow the naming convention.
- [ ] Placeholders in formatted strings are used correctly.
- [ ] The UI has been tested in different languages.
- [ ] Localized strings used in converters have been implemented.
- [ ] Related technical documentation has been updated.

---

## References

### Internal Documentation
- [ComExp Localization Implementation Summary](ComExp_Localization_Summary.md)
- [Services Feature Implementation Notes](Feature_Implementation/Services.md)
- [ComExp Feature Implementation Notes](Feature_Implementation/ComExp.md)

### External Resources
- [.resw file format](https://learn.microsoft.com/windows/uwp/app-resources/localize-strings-ui-manifest)
- [ResourceManager API](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.windows.applicationmodel.resources.resourcemanager)

---

**Last updated**: 2026-02-03
