using ManagementTools.Core.Features.PCManagement.Services.DevMgmt;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using ManagementTools.Core.Features.PCManagement.Services.FsMgmt;
using ManagementTools.Core.Features.PCManagement.Services.LusrMgr;
using ManagementTools.Core.Features.PCManagement.Services.PerfMon;
using ManagementTools.Core.Features.PCManagement.Services.WindowsServices;
using ManagementTools.Core.Features.PCManagement.ViewModels.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.ViewModels.DevMgmt;
using ManagementTools.Core.Features.PCManagement.ViewModels.EventViewer;
using ManagementTools.Core.Features.PCManagement.ViewModels.FsMgmt;
using ManagementTools.Core.Features.PCManagement.ViewModels.LusrMgr;
using ManagementTools.Core.Features.PCManagement.ViewModels.PerfMon;
using ManagementTools.Core.Features.PCManagement.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ManagementTools.Core.Features.PCManagement;

internal static class PCManagementModule
{
    internal static IServiceCollection AddPCManagement(this IServiceCollection services)
    {
        services.AddTransient<DeviceManagerService>();
        services.AddTransient<DiskManagementService>();
        services.AddTransient<EventViewerService>();
        services.AddTransient<SharedFoldersService>();
        services.AddTransient<LocalUserGroupManager>();
        services.AddTransient<PerformanceMonitorService>();
        services.AddTransient<WindowsServiceManager>();

        services.AddTransient<DeviceManagerViewModel>();
        services.AddTransient<DiskManagementViewModel>();
        services.AddTransient<EventViewerViewModel>();
        services.AddTransient<SharedFoldersViewModel>();
        services.AddTransient<LocalUsersGroupsViewModel>();
        services.AddTransient<PerformanceMonitorViewModel>();
        services.AddTransient<ServicesViewModel>();

        return services;
    }
}
