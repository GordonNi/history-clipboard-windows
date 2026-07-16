using System.Runtime.InteropServices;
using System.Windows.Interop;
using HistoryClipboard.App.Windows;

namespace HistoryClipboard.Tests;

public sealed partial class TaskbarRestartRecoveryControllerTests
{
    private const int TestTaskbarCreatedMessage = 0xC123;

    [Fact]
    public void MatchingMessageRepublishesAndUnrelatedMessageIsIgnored()
    {
        HwndSourceHook? installedHook = null;
        var republishCount = 0;
        using var controller = new TaskbarRestartRecoveryController(
            TestTaskbarCreatedMessage,
            hook => installedHook = hook,
            _ => { },
            () => republishCount++);

        Assert.NotNull(installedHook);
        Invoke(installedHook, TestTaskbarCreatedMessage - 1);
        Invoke(installedHook, TestTaskbarCreatedMessage);
        Invoke(installedHook, TestTaskbarCreatedMessage);

        Assert.Equal(2, republishCount);
    }

    [Fact]
    public void DisposeRemovesHookOnceAndPreventsQueuedRecovery()
    {
        HwndSourceHook? installedHook = null;
        HwndSourceHook? removedHook = null;
        var removeCount = 0;
        var republishCount = 0;
        var controller = new TaskbarRestartRecoveryController(
            TestTaskbarCreatedMessage,
            hook => installedHook = hook,
            hook =>
            {
                removedHook = hook;
                removeCount++;
            },
            () => republishCount++);

        controller.Dispose();
        controller.Dispose();
        Assert.NotNull(installedHook);
        Invoke(installedHook, TestTaskbarCreatedMessage);

        Assert.Same(installedHook, removedHook);
        Assert.Equal(1, removeCount);
        Assert.Equal(0, republishCount);
    }

    [Fact]
    public void RegisteredWindowsMessageInvokesRepublisherWithoutExplorerRestart()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var parameters = new HwndSourceParameters("HistoryClipboard.TaskbarRecoveryTest")
                {
                    Width = 1,
                    Height = 1,
                    WindowStyle = 0,
                };
                using var source = new HwndSource(parameters);
                var republishCount = 0;
                var controller = new TaskbarRestartRecoveryController(
                    source.Handle,
                    () => republishCount++);
                var taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

                Assert.NotEqual(0u, taskbarCreatedMessage);
                NativeMethods.SendMessage(
                    source.Handle,
                    taskbarCreatedMessage,
                    IntPtr.Zero,
                    IntPtr.Zero);
                Assert.Equal(1, republishCount);

                controller.Dispose();
                NativeMethods.SendMessage(
                    source.Handle,
                    taskbarCreatedMessage,
                    IntPtr.Zero,
                    IntPtr.Zero);
                Assert.Equal(1, republishCount);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The STA recovery test timed out.");
        Assert.Null(failure);
    }

    private static void Invoke(HwndSourceHook hook, int message)
    {
        var handled = false;
        hook(IntPtr.Zero, message, IntPtr.Zero, IntPtr.Zero, ref handled);
        Assert.False(handled);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW",
            StringMarshalling = StringMarshalling.Utf16)]
        internal static partial uint RegisterWindowMessage(string messageName);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static partial IntPtr SendMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wordParameter,
            IntPtr longParameter);
    }
}
