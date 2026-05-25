// ============================================================================
// Business Rule Validator
// ============================================================================
// Provides validation for AzMan business rule scripts (VBScript/JScript).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ManagementTools.Core.Features.UserSecurity.Services.AzMan;

/// <summary>
/// Business rule script language
/// </summary>
public enum BizRuleLanguage
{
    /// <summary>VBScript</summary>
    VBScript,
    /// <summary>JScript (JavaScript)</summary>
    JScript
}

/// <summary>
/// Business rule validation result
/// </summary>
public class BizRuleValidationResult
{
    /// <summary>Whether the script is valid</summary>
    public bool IsValid { get; set; }

    /// <summary>List of validation errors</summary>
    public List<string> Errors { get; set; } = [];

    /// <summary>List of validation warnings</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Detected script language</summary>
    public BizRuleLanguage? DetectedLanguage { get; set; }
}

/// <summary>
/// Validates AzMan business rule scripts
/// </summary>
public static class BizRuleValidator
{
    // Required AzMan objects that should be used in business rules
    private static readonly string[] AzManObjects = ["AzBizRuleContext"];

    // Common VBScript keywords
    private static readonly string[] VBScriptKeywords = [
        "Sub", "End Sub", "Function", "End Function", "Dim", "Set",
        "If", "Then", "Else", "End If", "For", "Next", "Do", "Loop",
        "While", "Wend", "Select", "Case", "End Select"
    ];

    // Common JScript keywords
    private static readonly string[] JScriptKeywords = [
        "function", "var", "let", "const", "if", "else", "for", "while",
        "do", "switch", "case", "break", "continue", "return", "try", "catch"
    ];

    /// <summary>
    /// Validate a business rule script
    /// </summary>
    /// <param name="script">The script content</param>
    /// <param name="language">The script language (VBScript or JScript)</param>
    /// <returns>Validation result</returns>
    public static BizRuleValidationResult Validate(string script, string language)
    {
        var result = new BizRuleValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(script))
        {
            result.IsValid = false;
            result.Errors.Add("Script cannot be empty.");
            return result;
        }

        // Determine language
        var lang = ParseLanguage(language);
        if (lang == null)
        {
            result.IsValid = false;
            result.Errors.Add($"Invalid script language: '{language}'. Must be 'VBScript' or 'JScript'.");
            return result;
        }
        result.DetectedLanguage = lang;

        // Check for AzBizRuleContext usage
        if (!script.Contains("AzBizRuleContext", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add("Script does not reference 'AzBizRuleContext'. Business rules typically use this object to set the authorization result.");
        }

        // Check for BusinessRuleResult assignment
        if (!script.Contains("BusinessRuleResult", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add("Script does not set 'BusinessRuleResult'. The rule may not affect authorization decisions.");
        }

        // Language-specific validation
        if (lang == BizRuleLanguage.VBScript)
        {
            ValidateVBScript(script, result);
        }
        else
        {
            ValidateJScript(script, result);
        }

        return result;
    }

    /// <summary>
    /// Detect the script language from content
    /// </summary>
    public static BizRuleLanguage? DetectLanguage(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return null;

        int vbScore = 0;
        int jsScore = 0;

        // Check for VBScript patterns
        foreach (var keyword in VBScriptKeywords)
        {
            if (Regex.IsMatch(script, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
            {
                vbScore++;
            }
        }

        // Check for JScript patterns
        foreach (var keyword in JScriptKeywords)
        {
            if (Regex.IsMatch(script, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.None))
            {
                jsScore++;
            }
        }

        // Additional patterns
        if (script.Contains("End Sub", StringComparison.OrdinalIgnoreCase) ||
            script.Contains("End Function", StringComparison.OrdinalIgnoreCase))
        {
            vbScore += 3;
        }

        if (script.Contains("function(") || script.Contains("function (") ||
            script.Contains("=>") || script.Contains("==="))
        {
            jsScore += 3;
        }

        if (vbScore > jsScore)
            return BizRuleLanguage.VBScript;
        if (jsScore > vbScore)
            return BizRuleLanguage.JScript;

        return null;
    }

    /// <summary>
    /// Parse language string to enum
    /// </summary>
    public static BizRuleLanguage? ParseLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        return language.Trim().ToUpperInvariant() switch
        {
            "VBSCRIPT" => BizRuleLanguage.VBScript,
            "JSCRIPT" => BizRuleLanguage.JScript,
            "JAVASCRIPT" => BizRuleLanguage.JScript,
            _ => null
        };
    }

    /// <summary>
    /// Get the language string for COM API
    /// </summary>
    public static string GetLanguageString(BizRuleLanguage language)
    {
        return language switch
        {
            BizRuleLanguage.VBScript => "VBScript",
            BizRuleLanguage.JScript => "JScript",
            _ => "VBScript"
        };
    }

    private static void ValidateVBScript(string script, BizRuleValidationResult result)
    {
        // Check for balanced Sub/End Sub
        int subCount = Regex.Matches(script, @"\bSub\b", RegexOptions.IgnoreCase).Count;
        int endSubCount = Regex.Matches(script, @"\bEnd\s+Sub\b", RegexOptions.IgnoreCase).Count;
        if (subCount != endSubCount)
        {
            result.IsValid = false;
            result.Errors.Add($"Unbalanced Sub/End Sub: {subCount} Sub(s) but {endSubCount} End Sub(s).");
        }

        // Check for balanced Function/End Function
        int funcCount = Regex.Matches(script, @"\bFunction\b", RegexOptions.IgnoreCase).Count;
        int endFuncCount = Regex.Matches(script, @"\bEnd\s+Function\b", RegexOptions.IgnoreCase).Count;
        if (funcCount != endFuncCount)
        {
            result.IsValid = false;
            result.Errors.Add($"Unbalanced Function/End Function: {funcCount} Function(s) but {endFuncCount} End Function(s).");
        }

        // Check for balanced If/End If
        int ifCount = Regex.Matches(script, @"\bIf\b.*\bThen\b", RegexOptions.IgnoreCase).Count;
        int endIfCount = Regex.Matches(script, @"\bEnd\s+If\b", RegexOptions.IgnoreCase).Count;
        // Note: Single-line If statements don't need End If, so we only warn if there are more End If than If
        if (endIfCount > ifCount)
        {
            result.Warnings.Add($"Possible unbalanced If/End If: {ifCount} If(s) but {endIfCount} End If(s).");
        }
    }

    private static void ValidateJScript(string script, BizRuleValidationResult result)
    {
        // Check for balanced braces
        int openBraces = 0;
        int closeBraces = 0;
        foreach (char c in script)
        {
            if (c == '{') openBraces++;
            else if (c == '}') closeBraces++;
        }
        if (openBraces != closeBraces)
        {
            result.IsValid = false;
            result.Errors.Add($"Unbalanced braces: {openBraces} opening but {closeBraces} closing.");
        }

        // Check for balanced parentheses
        int openParens = 0;
        int closeParens = 0;
        foreach (char c in script)
        {
            if (c == '(') openParens++;
            else if (c == ')') closeParens++;
        }
        if (openParens != closeParens)
        {
            result.IsValid = false;
            result.Errors.Add($"Unbalanced parentheses: {openParens} opening but {closeParens} closing.");
        }

        // Check for common syntax errors
        if (Regex.IsMatch(script, @";\s*;"))
        {
            result.Warnings.Add("Double semicolons detected. This may indicate missing code.");
        }
    }

    /// <summary>
    /// Generate a sample business rule script
    /// </summary>
    public static string GenerateSampleScript(BizRuleLanguage language, string description = "Sample business rule")
    {
        if (language == BizRuleLanguage.VBScript)
        {
            return $@"' {description}
' This script runs when the associated task/role is evaluated

Dim Result

' Get the AzBizRuleContext object
Set BizRuleContext = AzBizRuleContext

' Example: Check a parameter value
' If BizRuleContext.GetParameter(""Amount"") > 1000 Then
'     Result = False
' Else
'     Result = True
' End If

' Default: Allow access
Result = True

' Set the business rule result
BizRuleContext.BusinessRuleResult = Result
";
        }
        else
        {
            return $@"// {description}
// This script runs when the associated task/role is evaluated

// Get the AzBizRuleContext object
var BizRuleContext = AzBizRuleContext;

// Example: Check a parameter value
// var amount = BizRuleContext.GetParameter(""Amount"");
// var result = (amount <= 1000);

// Default: Allow access
var result = true;

// Set the business rule result
BizRuleContext.BusinessRuleResult = result;
";
        }
    }
}


