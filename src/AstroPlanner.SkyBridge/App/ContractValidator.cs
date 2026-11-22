using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AstroPlanner.SkyBridge.App;

/// <summary>
/// Checks a contract record against its DataAnnotations, and recursively every nested contract
/// record and list of them (<see cref="Validator"/> alone stops at the first level).
/// </summary>
internal static class ContractValidator
{
    public static bool IsValid(object? value) => IsValid(value, depth: 0);

    private static bool IsValid(object? value, int depth)
    {
        if (value is null)
        {
            return true;
        }

        if (depth > 16 || !Validator.TryValidateObject(value, new ValidationContext(value), validationResults: null, validateAllProperties: true))
        {
            return false;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            switch (property.GetValue(value))
            {
                case string:
                    break;
                case IEnumerable items:
                    foreach (var item in items)
                    {
                        if (IsContract(item) && !IsValid(item, depth + 1))
                        {
                            return false;
                        }
                    }

                    break;
                case { } nested when IsContract(nested):
                    if (!IsValid(nested, depth + 1))
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>Contracts are records (the compiler gives every record a <c>&lt;Clone&gt;$</c> method).</summary>
    private static bool IsContract(object? value) =>
        value is not null && value.GetType() is { IsClass: true } type && type.GetMethod("<Clone>$") is not null;
}
