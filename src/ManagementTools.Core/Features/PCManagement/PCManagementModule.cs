using ManagementTools.Core.Features.PCManagement.Services.DevMgmt;
using ManagementTools.Core.Features.PCManagement.Services.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.Services.EventViewer;
using ManagementTools.Core.Features.PCManagement.Services.FsMgmt;
using ManagementTools.Core.Features.PCManagement.Services.LusrMgr;
using ManagementTools.Core.Features.PCManagement.Services.PerfMon;
using ManagementTools.Core.Features.PCManagement.Services.TaskSchd;
using ManagementTools.Core.Features.PCManagement.Services.WindowsServices;
using ManagementTools.Core.Features.PCManagement.ViewModels.DiskMgmt;
using ManagementTools.Core.Features.PCManagement.ViewModels.DevMgmt;
using ManagementTools.Core.Features.PCManagement.ViewModels.EventViewer;
using ManagementTools.Core.Features.PCManagement.ViewModels.FsMgmt;
using ManagementTools.Core.Features.PCManagement.ViewModels.LusrMgr;
using ManagementTools.Core.Features.PCManagement.ViewModels.PerfMon;
using ManagementTools.Core.Features.PCManagement.ViewModels.Services;
using ManagementTools.Core.Features.PCManagement.ViewModels.TaskSchd;
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

        // Task Scheduler service holds a dedicated STA COM thread + cached connection, so it is a singleton.
        services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
        services.AddTransient<TaskHistoryService>();

        services.AddTransient<DeviceManagerViewModel>();
        services.AddTransient<DiskManagementViewModel>();
        services.AddTransient<EventViewerViewModel>();
        services.AddTransient<SharedFoldersViewModel>();
        services.AddTransient<LocalUsersGroupsViewModel>();
        services.AddTransient<PerformanceMonitorViewModel>();
        services.AddTransient<ServicesViewModel>();
        services.AddTransient<TaskSchedulerViewModel>();

        return services;
    }
}
