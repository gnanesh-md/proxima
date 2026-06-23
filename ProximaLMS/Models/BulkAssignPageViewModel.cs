using System;
using System.Collections.Generic;

namespace ProximaLMS.Models
{
    public class BulkAssignPageViewModel
    {
        public string UserName { get; set; } = "";
        public bool IsManager { get; set; }
        public int TeamSize { get; set; }
        public bool IsAdminView { get; set; }   // admin sees completion across ALL assignments
    }
}
