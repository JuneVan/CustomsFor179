namespace CustomsFor179Client
{
    public class InquiryTaskWorker : BackgroundService
    {
        private readonly ILogger<InquiryTaskWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        public InquiryTaskWorker(ILogger<InquiryTaskWorker> logger,
             IServiceScopeFactory serviceScopeFactory,
             IConfiguration configuration)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // ʹ�÷�Χ����ע���Ա����ݿ��ͷ�
                using var scope = _serviceScopeFactory.CreateScope();
                // ��ѯ����
                var inquiryTaskService = scope.ServiceProvider.GetRequiredService<IInquiryTaskService>();
                var tasks = await inquiryTaskService.GetUnhandleTasks(10);
                _logger.LogInformation($"��鵽{tasks.Count()}�����ز�ѯ����");
                if (tasks.Any())
                {
                    foreach (var task in tasks)
                    {
                        ThreadPool.QueueUserWorkItem(async task =>
                        {
                            using var scope2 = _serviceScopeFactory.CreateScope();
                            var inquiryTaskService2 = scope2.ServiceProvider.GetRequiredService<IInquiryTaskService>();
                            try
                            {
                                _logger.LogInformation($"����OrderNo:{task.OrderNo}/SessionId:{task.SessionId}����ʼ����");
                                // ��ѯ��������
                                var realTimeDataService = scope2.ServiceProvider.GetRequiredService<IRealTimeDataService>();
                                var realTimeData = await realTimeDataService.Get(task.OrderNo);
                                if (realTimeData == null)
                                    throw new ArgumentNullException("��ѯʵʱ����Ϊ�ա�");

                                realTimeData.ServiceTime = DateTime.Now.ToTimestamp();
                                realTimeData.SessionId = task.SessionId;
                                realTimeData.CertNo = _configuration["AppSettings:CertNo"];
                                _logger.LogInformation($"��ȡ�Ķ������ݣ�{JsonConvert.SerializeObject(realTimeData)}");

                                // ǩ�� 
                                var signatureService = scope2.ServiceProvider.GetRequiredService<ISignatureService>();
                                var signValue = await signatureService.Sign(realTimeData);
                                if (string.IsNullOrEmpty(signValue))
                                    throw new InvalidOperationException($"��Чǩ����");
                                realTimeData.SignValue = signValue;

                                // ����֪ͨ����
                                var realTimeDataUpSender = scope2.ServiceProvider.GetRequiredService<RealTimeDataUpSender>();
                                await realTimeDataUpSender.SendAsync(realTimeData);

                                // ��������״̬
                                task.SetSuccess();
                                await inquiryTaskService2.Update(task);
                                _logger.LogInformation($"����OrderNo:{task.OrderNo}/SessionId:{task.SessionId}����ɴ���");

                            }
                            catch (Exception ex)
                            {
                                _logger.LogInformation($"����OrderNo:{task.OrderNo}/SessionId:{task.SessionId}���������쳣���쳣��Ϣ��{ex.Message}��");
                                task.SetFail(ex.Message);
                                await inquiryTaskService2.Update(task);
                            }
                        }, task, true); 
                    }
                }
                // һ����
                await Task.Delay(1000 * 60, stoppingToken);
            }
        }
    }
}