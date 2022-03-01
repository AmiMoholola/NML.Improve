using System;
using System.Linq;
using Nml.Improve.Me.Dependencies;

namespace Nml.Improve.Me
{
    public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
    {
        private readonly IDataContext DataContext;
        private IPathProvider _templatePathProvider;
        public IViewGenerator View_Generator;
        internal readonly IConfiguration _configuration;
        private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
        private readonly IPdfGenerator _pdfGenerator;

        public PdfApplicationDocumentGenerator(
            IDataContext dataContext,
            IPathProvider templatePathProvider,
            IViewGenerator viewGenerator,
            IConfiguration configuration,
            IPdfGenerator pdfGenerator,
            ILogger<PdfApplicationDocumentGenerator> logger)
        {
            if (dataContext != null)
                throw new ArgumentNullException(nameof(dataContext));

            DataContext = dataContext;
            _templatePathProvider = templatePathProvider ?? throw new ArgumentNullException("templatePathProvider");
            View_Generator = viewGenerator;
            _configuration = configuration;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pdfGenerator = pdfGenerator;
        }

        public byte[] Generate(Guid applicationId, string baseUri)
        {
            Application application = DataContext.Applications.Single(app => app.Id == applicationId);

            if (application != null)
            {
                if (baseUri.EndsWith("/"))
                    baseUri = baseUri.Substring(baseUri.Length - 1);

                string view;
                ApplicationViewModel viewModel = null;

                switch (application.State)
                {
                    case ApplicationState.Pending:
                        viewModel = CreatePendingApplicationViewModel(application);
                        break;
                    case ApplicationState.Activated:
                        viewModel = CreateActivatedApplicationViewModel(application);
                        break;
                    case ApplicationState.InReview:
                        viewModel = CreateInReviewApplicationViewModel(application);
                        break;
                    default:
                        _logger.LogWarning(
$"The application is in state '{application.State}' and no valid document can be generated for it.");
                        return null;
                }

                string templatePath = _templatePathProvider.Get($"{application.State.ToString()}Application");
                view = View_Generator.GenerateFromPath($"{baseUri}{templatePath}", viewModel);

                var pdfOptions = new PdfOptions
                {
                    PageNumbers = PageNumbers.Numeric,
                    HeaderOptions = new HeaderOptions
                    {
                        HeaderRepeat = HeaderRepeat.FirstPageOnly,
                        HeaderHtml = PdfConstants.Header
                    }
                };
                var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
                return pdf.ToBytes();
            }
            else
            {
                _logger.LogWarning(
                    $"No application found for id '{applicationId}'");
                return null;
            }
        }

        private T CreateApplicationViewModel<T>(Application application) where T : ApplicationViewModel, new()
        {
            var viewModel = new T()
            {
                ReferenceNumber = application.ReferenceNumber,
                State = application.State.ToDescription(),
                FullName = $"{application.Person.FirstName} {application.Person.Surname}",
                AppliedOn = application.Date,
                SupportEmail = _configuration.SupportEmail,
                Signature = _configuration.Signature
            };
            return viewModel;
        }

        private void UpdateActivatedApplicationViewModel(ApplicationViewModel viewModel, Application application)
        {
            viewModel.LegalEntity = application.IsLegalEntity ? application.LegalEntity : null;
            viewModel.PortfolioFunds = application.Products.SelectMany(p => p.Funds);
            viewModel.PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
                .Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
                .Sum();
        }

        private InReviewApplicationViewModel CreateInReviewApplicationViewModel(Application application)
        {
            var inReviewMessage = "Your application has been placed in review" +
                                        application.CurrentReview.Reason switch
                                        {
                                            { } reason when reason.Contains("address") =>
                                                " pending outstanding address verification for FICA purposes.",
                                            { } reason when reason.Contains("bank") =>
                                                " pending outstanding bank account verification.",
                                            _ =>
                                                " because of suspicious account behaviour. Please contact support ASAP."
                                        };
            var viewModel = CreateApplicationViewModel<InReviewApplicationViewModel>(application);
            UpdateActivatedApplicationViewModel(viewModel, application);
            viewModel.InReviewMessage = inReviewMessage;
            viewModel.InReviewInformation = application.CurrentReview;
            return viewModel;
        }

        private ActivatedApplicationViewModel CreateActivatedApplicationViewModel(Application application)
        {
            var viewModel = CreateApplicationViewModel<ActivatedApplicationViewModel>(application);
            UpdateActivatedApplicationViewModel(viewModel, application);
            return viewModel;
        }

        private PendingApplicationViewModel CreatePendingApplicationViewModel(Application application)
        {
            return CreateApplicationViewModel<PendingApplicationViewModel>(application);
        }
    }
}
