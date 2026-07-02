using OneMMC.Core.Features.PCManagement.Models.DiskMgmt;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OneMMC.Converters
{
    /// <summary>
    /// Selects the appropriate DataTemplate based on the type of disk item.
    /// Used in Disk Management to display different UI layouts for physical disks vs CD-ROM drives.
    /// </summary>
    public partial class DiskItemTemplateSelector : DataTemplateSelector
    {
        /// <summary>
        /// Gets or sets the template used for physical disk items.
        /// </summary>
        public DataTemplate? PhysicalDiskTemplate { get; set; }

        /// <summary>
        /// Gets or sets the template used for CD-ROM drive items.
        /// </summary>
        public DataTemplate? CDROMTemplate { get; set; }

        /// <summary>
        /// Selects the appropriate template based on the item type.
        /// </summary>
        /// <param name="item">The disk item to select a template for.</param>
        /// <returns>PhysicalDiskTemplate for PhysicalDiskInfo, CDROMTemplate for CDROMInfo, or base template otherwise.</returns>
        protected override DataTemplate? SelectTemplateCore(object item)
        {
            return item switch
            {
                PhysicalDiskInfo => PhysicalDiskTemplate,
                CDROMInfo => CDROMTemplate,
                _ => base.SelectTemplateCore(item)
            };
        }

        /// <summary>
        /// Overload that delegates to the main SelectTemplateCore method.
        /// </summary>
        /// <param name="item">The disk item to select a template for.</param>
        /// <param name="container">The container element (not used).</param>
        /// <returns>The selected DataTemplate.</returns>
        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        {
            return SelectTemplateCore(item);
        }
    }
}

