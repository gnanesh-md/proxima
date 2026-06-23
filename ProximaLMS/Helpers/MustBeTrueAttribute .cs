using System.ComponentModel.DataAnnotations;

namespace ProximaLMS.Models
{
    public class MustBeTrueAttribute : ValidationAttribute
    {
        public override bool IsValid(object value)
        {
            if (value is bool boolValue)
            {
                return boolValue; // must be true
            }
            return false;
        }
    }
}
