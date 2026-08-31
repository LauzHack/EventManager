using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using EventManager.Abstractions;
using EventManager.Models;
using EventManager.Tests.TestInfrastructure;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventManager.Tests.Abstractions;

[TestClass]
public sealed class OperationTests : TestsBase
{
    [TestMethod]
    public void DefaultViewWithoutUserRoundtrips()
    {
        var operation = Operation.Parse(new Uri("https://example.org/", UriKind.Absolute));
        AssertRoundtrips(operation);
    }

    [TestMethod]
    public void DefaultViewRoundtrips()
    {
        var operation = Operation.CreatePageView<Participant>();
        AssertRoundtrips(operation);
    }

    [TestMethod]
    public void ViewRoundtrips()
    {
        var operation = Operation.CreatePageView<Participant, FakePage>();
        AssertRoundtrips(operation);
    }

    [TestMethod]
    public void ViewWithArgumentsRoundtrips()
    {
        var operation = Operation.CreatePageView<Participant, FakePage>();
        operation = operation.WithExtraTextArgument("hello", "world");
        AssertRoundtrips(operation);
    }

    [TestMethod]
    public void ActionRoundtrips()
    {
        var operation = Operation.CreatePageAction<Participant, FakePage>(nameof(FakePage.EditAsync), ("n", "42"), ("s", "hello"));
        AssertRoundtrips(operation);
    }

    [TestMethod]
    [DataRow("some-id")]
    [DataRow("some id")]
    [DataRow("some / id with/slashes")]
    public void FileViewRoundtrips(string id)
    {
        var operation = Operation.CreateFileView(id);
        AssertRoundtrips(operation);
    }

    [TestMethod]
    [DataRow("some-id")]
    [DataRow("some id")]
    [DataRow("some    id")]
    public void LetterViewRoundtrips(string id)
    {
        var operation = Operation.CreateLetterView(id);
        AssertRoundtrips(operation);
    }

    [TestMethod]
    public void ProjectViewRoundtrips()
    {
        var operation = Operation.Parse(new Uri("https://example.org/projects", UriKind.Absolute));
        AssertRoundtrips(operation);
    }

    private static void AssertRoundtrips(Operation operation)
    {
        var baseUri = new Uri("https://example.org", UriKind.Absolute);
        var uri = new Uri(baseUri, operation.RelativeUri);
        var roundtripped = Operation.Parse(uri);
        Assert.AreEqual(operation, roundtripped);
    }


    [TestMethod]
    [DataRow(typeof(SummaryOnlyPage))]
    [DataRow(typeof(EditablePage))]
    [DataRow(typeof(EditablePage2))]
    public async Task DefaultViewUsesFirstRequiredPage(Type first)
    {
        var operation = Operation.CreatePageView<Participant>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(first, typeof(RequiredPage), typeof(RequiredPage2)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Page>(result);
        Assert.AreEqual(Status.None, page.Status);
        Assert.AreEqual("", page.Message);
        Assert.IsNotNull(page.View);
        Assert.IsInstanceOfType<RequiredPage>(page.View.Page);
        Assert.HasCount(2, page.AvailableViews);
        Assert.IsInstanceOfType(page.AvailableViews[0].Page, first);
        Assert.IsInstanceOfType<RequiredPage>(page.AvailableViews[1].Page);
    }

    [TestMethod]
    public async Task DefaultViewUsesFirstRequiredPageWithoutUser()
    {
        var operation = Operation.CreatePageView<Participant>();
        var result = await operation.ExecuteAsync(null, Pages(typeof(RequiredPageWithoutUser), typeof(RequiredPage)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Page>(result);
        Assert.AreEqual(Status.None, page.Status);
        Assert.AreEqual("", page.Message);
        Assert.IsNotNull(page.View);
        Assert.IsInstanceOfType<RequiredPageWithoutUser>(page.View.Page);
        var loneView = Assert.ContainsSingle(page.AvailableViews);
        Assert.AreEqual(page.View, loneView);
    }

    [TestMethod]
    public async Task ViewReturnsUnavailableForSummaryOnlyPage()
    {
        var operation = Operation.CreatePageView<Participant, SummaryOnlyPage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(SummaryOnlyPage)), await CreateDependenciesAsync());

        Assert.IsInstanceOfType<OperationResult.Unavailable>(result);
    }

    [TestMethod]
    public async Task ViewUsesPageWhenEditable()
    {
        var operation = Operation.CreatePageView<Participant, EditablePage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(EditablePage)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Page>(result);
        Assert.IsNotNull(page.View);
        Assert.IsInstanceOfType<EditablePage>(page.View.Page);
    }

    [TestMethod]
    public async Task ViewUsesPageWhenRequired()
    {
        var operation = Operation.CreatePageView<Participant, RequiredPage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(RequiredPage)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Page>(result);
        Assert.IsNotNull(page.View);
        Assert.IsInstanceOfType<RequiredPage>(page.View.Page);
    }

    [TestMethod]
    public async Task ViewReturnsUnavailableForForbiddenPage()
    {
        var operation = Operation.CreatePageView<Participant, ForbiddenPage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(ForbiddenPage)), await CreateDependenciesAsync());

        Assert.IsInstanceOfType<OperationResult.Unavailable>(result);
    }

    [TestMethod]
    public async Task ViewReturnsUnavailableForForbiddenPageAfterRequiredPage()
    {
        var operation = Operation.CreatePageView<Participant, ForbiddenPage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(RequiredPage), typeof(ForbiddenPage)), await CreateDependenciesAsync());

        // this should specifically not be "not found", to provide a better user experience
        Assert.IsInstanceOfType<OperationResult.Unavailable>(result);
    }

    [TestMethod]
    [DataRow(typeof(SummaryOnlyPage))]
    [DataRow(typeof(EditablePage))]
    public async Task ViewIncludesNonHiddenPreviousPages(Type first)
    {
        var operation = Operation.CreatePageView<Participant, RequiredPage2>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(first, typeof(RequiredPage2)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Page>(result);
        Assert.IsNotNull(page.View);
        Assert.IsInstanceOfType<RequiredPage2>(page.View.Page);
        Assert.HasCount(2, page.AvailableViews);
        Assert.IsInstanceOfType(page.AvailableViews[0].Page, first);
        Assert.IsInstanceOfType<RequiredPage2>(page.AvailableViews[1].Page);
    }

    [TestMethod]
    public async Task ViewIncludesNonHiddenOtherPages()
    {
        var operation = Operation.CreatePageView<Participant, EditablePage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(EditablePage), typeof(EditablePage2)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Page>(result);
        Assert.IsNotNull(page.View);
        Assert.IsInstanceOfType<EditablePage>(page.View.Page);
        Assert.HasCount(2, page.AvailableViews);
        Assert.IsInstanceOfType<EditablePage>(page.AvailableViews[0].Page);
        Assert.IsInstanceOfType<EditablePage2>(page.AvailableViews[1].Page);
    }

    [TestMethod]
    public async Task ViewReturnsNotFoundForUnknownPage()
    {
        var operation = Operation.CreatePageView<Participant, EditablePage>();
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(), await CreateDependenciesAsync());

        Assert.IsInstanceOfType<OperationResult.NotFound>(result);
    }

    [TestMethod]
    public async Task ActionReturnsUnavailableForSummaryOnlyPage()
    {
        var operation = Operation.CreatePageAction<Participant, SummaryOnlyPage>(nameof(SummaryOnlyPage.EditAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(SummaryOnlyPage)), await CreateDependenciesAsync());

        Assert.IsInstanceOfType<OperationResult.Unavailable>(result);
    }

    [TestMethod]
    public async Task ActionReturnsUnavailableForForbiddenPage()
    {
        var operation = Operation.CreatePageAction<Participant, ForbiddenPage>(nameof(ForbiddenPage.SayNameAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(ForbiddenPage)), await CreateDependenciesAsync());

        Assert.IsInstanceOfType<OperationResult.Unavailable>(result);
    }

    [TestMethod]
    public async Task ActionReturnsUnavailableForForbiddenPageAfterRequiredPage()
    {
        var operation = Operation.CreatePageAction<Participant, ForbiddenPage>(nameof(ForbiddenPage.SayNameAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(RequiredPage), typeof(ForbiddenPage)), await CreateDependenciesAsync());

        // this should specifically not be "not found", to provide a better user experience
        Assert.IsInstanceOfType<OperationResult.Unavailable>(result);
    }

    [TestMethod]
    public async Task ActionUsesPageWhenEditable()
    {
        var operation = Operation.CreatePageAction<Participant, EditablePage>(nameof(EditablePage.SayNameAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(EditablePage)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Action>(result);
        Assert.AreEqual(typeof(EditablePage).Name, page.Message);
    }

    [TestMethod]
    public async Task ActionUsesPageWhenRequired()
    {
        var operation = Operation.CreatePageAction<Participant, RequiredPage>(nameof(RequiredPage.SayNameAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(RequiredPage)), await CreateDependenciesAsync());

        var page = Assert.IsInstanceOfType<OperationResult.Action>(result);
        Assert.AreEqual(typeof(RequiredPage).Name, page.Message);
    }

    [TestMethod]
    public async Task ActionReturnsNotFoundForUnknownPage()
    {
        var operation = Operation.CreatePageAction<Participant, EditablePage>(nameof(EditablePage.SayNameAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(), await CreateDependenciesAsync());

        Assert.IsInstanceOfType<OperationResult.NotFound>(result);
    }

    [TestMethod]
    public async Task ActionReturnsFileWhenMethodDoes()
    {
        var operation = Operation.CreatePageAction<Participant, FilePage>(nameof(FilePage.DownloadAsync));
        var result = await operation.ExecuteAsync(new Participant("x@example.org"), Pages(typeof(FilePage)), await CreateDependenciesAsync());

        var fileResult = Assert.IsInstanceOfType<OperationResult.File>(result);
        Assert.AreEqual("example.txt", fileResult.RequestedFile.Name);
        var bytes = await fileResult.RequestedFile.ReadAsBytesAsync();
        Assert.AreSequenceEqual<byte>([0, 1, 2, 3], bytes);
    }

    [TestMethod]
    public void CreatePageActionFailsForUnknownMethod()
    {
        Assert.Throws<ArgumentException>(() => Operation.CreatePageAction<Participant, FakePage>("DoesNotExist"));
    }


    private Task<SystemDependencies> CreateDependenciesAsync()
        => SystemDependencies.CreateAsync(Db, FileStorage, c => DisabledEmailSender, TimeProvider);

    private sealed class FakePage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => RequiredView("Fake");

        public async Task<StatusMessage> EditAsync(int n, string s)
            => Success(s + n.ToString(CultureInfo.InvariantCulture));

        public async Task<StatusMessage> SayHelloAsync()
            => Success("Hello");

        public async Task<StatusMessage> DoNothingAsync()
            => NoChange();

        public async Task<StatusMessage> ErrorAsync()
            => Error("Oh no");

        public async Task<StatusMessage> ThrowExceptionAsync()
            => throw new InvalidOperationException("Oh no, from " + GetType());
    }

    private sealed class RequiredPageWithoutUser : Page<Participant?>
    {
        public override async Task<PageView> ViewAsync(Participant? participant)
            => RequiredView("Required");
    }

    private sealed class RequiredPage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => RequiredView("Required");

        public async Task<StatusMessage> SayNameAsync()
            => Success(GetType().Name);
    }

    private sealed class RequiredPage2 : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => RequiredView("Required2");
    }

    private sealed class EditablePage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => EditableView("Editable", "Edit");

        public async Task<StatusMessage> SayNameAsync()
            => Success(GetType().Name);
    }

    private sealed class EditablePage2 : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => EditableViaLinkView("Editable2");

        public async Task<StatusMessage> SayNameAsync()
            => Success(GetType().Name);
    }

    private sealed class SummaryOnlyPage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => SummaryOnlyView("SummaryOnly", ("Example", "42"));

        public async Task<StatusMessage> EditAsync()
            => NoChange();
    }

    private sealed class ForbiddenPage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => ForbiddenView();

        public async Task<StatusMessage> SayNameAsync()
            => Success(GetType().Name);
    }

    private sealed class FilePage : Page<Participant>
    {
        public override async Task<PageView> ViewAsync(Participant participant)
            => EditableView("Title", "Action");

        public async Task<File> DownloadAsync()
            => new File.InMemory("example.txt", "text/plain", [0, 1, 2, 3]);
    }

    private static SystemPages Pages(params Type[] pageTypes)
        => new(new Dictionary<Type, IReadOnlyCollection<Type>> { { typeof(Participant), pageTypes } });
}