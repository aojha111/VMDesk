using System.Reflection;
using System.Runtime.InteropServices;

namespace VMDesk.Rdp.Interop;

/// <summary>
/// Optional-property access against COM RCWs. Different Windows builds expose different
/// RDP interface properties; missing members degrade gracefully (spec §74).
/// </summary>
public static class RcwHelper
{
    public static bool TrySet(object comObject, string property, object value)
    {
        try
        {
            comObject.GetType().InvokeMember(property, BindingFlags.SetProperty, null, comObject, new[] { value });
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        catch (MissingMemberException)
        {
            return false;
        }
    }

    public static T? TryGet<T>(object comObject, string property)
    {
        try
        {
            var value = comObject.GetType().InvokeMember(property, BindingFlags.GetProperty, null, comObject, Array.Empty<object>());
            if (value is T typed)
            {
                return typed;
            }
        }
        catch (TargetInvocationException)
        {
        }
        catch (COMException)
        {
        }
        catch (MissingMemberException)
        {
        }

        return default;
    }

    public static bool TryCall(object comObject, string method, params object[] args)
    {
        try
        {
            comObject.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, comObject, args);
            return true;
        }
        catch (TargetInvocationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        catch (MissingMemberException)
        {
            return false;
        }
    }
}
