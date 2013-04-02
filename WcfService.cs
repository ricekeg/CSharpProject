using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ServiceModel;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;

namespace StarGps.Common
{
    /// <summary>
    /// WCF服务端启动辅助类
    /// </summary>
    public class WcfService : IDisposable
    {
        #region 私有属性
        private Type _type;//服务类型
        private string _config;//服务参数配置文件
        private ServiceHost _host;
        private WcfSettingConfig _setting = null;
        #endregion

        #region 公共属性
        /// <summary>
        /// 服务成功开启事件
        /// </summary>
        public EventHandler ServerOpened;
        /// <summary>
        /// 服务发生错误事件
        /// </summary>
        public EventHandler ServerFaulted;
        /// <summary>
        /// 服务成功关闭事件
        /// </summary>
        public EventHandler ServerClosed;
        #endregion

        #region 构造函数

        public WcfService(Type type)
        {
            _type = type;
        }
        public WcfService(WcfSettingConfig set)
        {
            _type = set.ImplementsContractType;
            _setting = set;
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 开启服务
        /// </summary>
        /// <param name="validate">服务权限验证方法</param>
        public void Open(Func<Dictionary<string, string>, int> validate = null)
        {
            try
            {
                if (_host == null)
                {
                    if(_setting.PortSharingEnabled)
                    WindowServiceHelper.StartNetTcpSharingService();
                    Close();
                    _host = new ServiceHost(_type);

                    LoadCustomConfig();

                    AddDiscoveryService();

                    if (validate != null)
                    {
                        foreach (var sep in _host.Description.Endpoints)
                        {
                            sep.Behaviors.Add(new AttachAuthBehavior(validate));
                        }
                    }
                    _host.Opened += delegate(object sender, EventArgs e)
                    {
                        if (ServerOpened != null) ServerOpened(sender, e);
                    };
                    _host.Faulted += delegate(object sender, EventArgs e)
                    {
                        if (ServerFaulted != null) ServerFaulted(sender, new ExceptionEventArgs() { ErrorInfo = new Exception("开启服务时发生异常："+e.ToString()) });
                    }; 
                    _host.Closed += delegate(object sender, EventArgs e)
                    {
                        if (ServerClosed != null) ServerClosed(sender, e);
                    };
                    _host.Open();
                }
            }
            catch (Exception ex)
            {
                if (ServerFaulted != null) { ServerFaulted(this, new ExceptionEventArgs() { ErrorInfo = ex }); }
            }
        }

        /// <summary>
        /// 关闭服务
        /// </summary>
        public void Close()
        {
            try
            {
                if (_host != null && _host.State == CommunicationState.Opened)
                    _host.Close();
            }
            catch (Exception ex)
            {
                if (ServerFaulted != null) { ServerFaulted(this, new ExceptionEventArgs() { ErrorInfo = ex }); }
            }
        }
        #endregion

        #region 私有方法

        private void AddDiscoveryService()
        {
            #region 上下线通知
            System.ServiceModel.Discovery.ServiceDiscoveryBehavior discover = _host.Description.Behaviors.Find<System.ServiceModel.Discovery.ServiceDiscoveryBehavior>();
            if (null == discover)
            {
                discover = new System.ServiceModel.Discovery.ServiceDiscoveryBehavior();
                _host.Description.Behaviors.Add(discover);
            }
            discover.AnnouncementEndpoints.Clear();
            discover.AnnouncementEndpoints.Add(new System.ServiceModel.Discovery.UdpAnnouncementEndpoint());//服务端主动通知
            bool isaddUdpDiscoveryEndpint = false;
            foreach (var item in _host.Description.Endpoints)
            {
                if (item is System.ServiceModel.Discovery.UdpDiscoveryEndpoint)
                {
                    isaddUdpDiscoveryEndpint = true; break;
                }
            }
            if (!isaddUdpDiscoveryEndpint)
            _host.AddServiceEndpoint(new System.ServiceModel.Discovery.UdpDiscoveryEndpoint());//客户端主动发现
            #endregion
        }

        private void LoadCustomConfig()
        {
            if (_setting != null)
            {
                _host.Description.Endpoints.Clear();
                for (int i = 0; i < _host.Description.Behaviors.Count; i++)
                {
                    if (_host.Description.Behaviors[i] is ServiceBehaviorAttribute)
                        ;
                    else if (_host.Description.Behaviors[i] is ServiceAuthenticationBehavior)
                        ;
                    else if (_host.Description.Behaviors[i] is ServiceAuthorizationBehavior)
                        ;
                    else
                    {
                        _host.Description.Behaviors.Remove(_host.Description.Behaviors[i]); i = 0;
                    }
                }
                foreach (ServiceElement service in _setting.ServiceConfig.Services)
                {
                    #region 添加服务
                    foreach (ServiceEndpointElement item in service.Endpoints)
                    {
                        ServiceEndpoint serviceTemp = _host.Description.Endpoints.Find(item.Address);
                        #region netTcpBinding
                        if (item.Binding.Equals("netTcpBinding", StringComparison.OrdinalIgnoreCase))
                        {
                            //System.ServiceModel.Channels.TcpTransportBindingElement tcptbe = new System.ServiceModel.Channels.TcpTransportBindingElement();
                            //tcptbe.ConnectionPoolSettings.MaxOutboundConnectionsPerEndpoint = 100;//默认值为10
                            NetTcpBinding nettcp = new NetTcpBinding(SecurityMode.None);
                            if (_setting.BindingConfig.NetTcpBinding.ContainsKey(item.BindingConfiguration))
                            {
                                NetTcpBindingElement ele = _setting.BindingConfig.NetTcpBinding.Bindings[item.BindingConfiguration];
                                nettcp.OpenTimeout = ele.OpenTimeout;
                                nettcp.CloseTimeout = ele.CloseTimeout;
                                nettcp.SendTimeout = ele.SendTimeout;
                                nettcp.ReceiveTimeout = ele.ReceiveTimeout;
                                nettcp.MaxReceivedMessageSize = ele.MaxReceivedMessageSize;
                                nettcp.MaxBufferSize = ele.MaxBufferSize;
                                nettcp.MaxBufferPoolSize = ele.MaxBufferPoolSize;
                                nettcp.PortSharingEnabled = ele.PortSharingEnabled;
                                nettcp.ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas();
                                nettcp.ReaderQuotas.MaxArrayLength = ele.ReaderQuotas.MaxArrayLength;
                                nettcp.ReaderQuotas.MaxStringContentLength = ele.ReaderQuotas.MaxStringContentLength;
                                nettcp.ReaderQuotas.MaxDepth = ele.ReaderQuotas.MaxDepth;
                                nettcp.ReaderQuotas.MaxBytesPerRead = ele.ReaderQuotas.MaxBytesPerRead;
                                nettcp.ReaderQuotas.MaxNameTableCharCount = ele.ReaderQuotas.MaxNameTableCharCount;
                                nettcp.Security.Mode = ele.Security.Mode;
                            }
                            if (serviceTemp == null)
                                _host.AddServiceEndpoint(item.Contract, nettcp, item.Address);
                            else
                                serviceTemp.Binding = nettcp;
                        }
                        #endregion

                        #region WSDualHttpBinding
                        else if (item.Binding.Equals("WSDualHttpBinding", StringComparison.OrdinalIgnoreCase))
                        {
                            WSDualHttpBinding wsdual = new WSDualHttpBinding(WSDualHttpSecurityMode.None);
                            if (_setting.BindingConfig.WSDualHttpBinding.ContainsKey(item.BindingConfiguration))
                            {
                                WSDualHttpBindingElement ele = _setting.BindingConfig.WSDualHttpBinding.Bindings[item.BindingConfiguration];
                                wsdual.OpenTimeout = ele.OpenTimeout;
                                wsdual.CloseTimeout = ele.CloseTimeout;
                                wsdual.SendTimeout = ele.SendTimeout;
                                wsdual.ReceiveTimeout = ele.ReceiveTimeout;
                                wsdual.MaxReceivedMessageSize = ele.MaxReceivedMessageSize;
                                wsdual.ClientBaseAddress = ele.ClientBaseAddress;
                                wsdual.MaxBufferPoolSize = ele.MaxBufferPoolSize;
                                wsdual.ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas();
                                wsdual.ReaderQuotas.MaxArrayLength = ele.ReaderQuotas.MaxArrayLength;
                                wsdual.ReaderQuotas.MaxStringContentLength = ele.ReaderQuotas.MaxStringContentLength;
                                wsdual.ReaderQuotas.MaxDepth = ele.ReaderQuotas.MaxDepth;
                                wsdual.ReaderQuotas.MaxBytesPerRead = ele.ReaderQuotas.MaxBytesPerRead;
                                wsdual.ReaderQuotas.MaxNameTableCharCount = ele.ReaderQuotas.MaxNameTableCharCount;
                                wsdual.UseDefaultWebProxy = ele.UseDefaultWebProxy;
                                wsdual.Security.Mode = ele.Security.Mode;
                            }
                            if (serviceTemp == null)
                                _host.AddServiceEndpoint(item.Contract, wsdual, item.Address);
                            else
                                serviceTemp.Binding = wsdual;
                        }
                        #endregion

                        #region BasicHttpBinding
                        else if (item.Binding.Equals("BasicHttpBinding", StringComparison.OrdinalIgnoreCase))
                        {
                            BasicHttpBinding wsdual = new BasicHttpBinding();
                            if (_setting.BindingConfig.BasicHttpBinding.ContainsKey(item.BindingConfiguration))
                            {
                                BasicHttpBindingElement ele = _setting.BindingConfig.BasicHttpBinding.Bindings[item.BindingConfiguration];
                                wsdual.OpenTimeout = ele.OpenTimeout;
                                wsdual.CloseTimeout = ele.CloseTimeout;
                                wsdual.SendTimeout = ele.SendTimeout;
                                wsdual.ReceiveTimeout = ele.ReceiveTimeout;
                                wsdual.MaxReceivedMessageSize = ele.MaxReceivedMessageSize;
                                wsdual.MaxBufferSize = ele.MaxBufferSize;
                                wsdual.MaxBufferPoolSize = ele.MaxBufferPoolSize;
                                wsdual.ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas();
                                wsdual.ReaderQuotas.MaxArrayLength = ele.ReaderQuotas.MaxArrayLength;
                                wsdual.ReaderQuotas.MaxStringContentLength = ele.ReaderQuotas.MaxStringContentLength;
                                wsdual.ReaderQuotas.MaxDepth = ele.ReaderQuotas.MaxDepth;
                                wsdual.ReaderQuotas.MaxBytesPerRead = ele.ReaderQuotas.MaxBytesPerRead;
                                wsdual.ReaderQuotas.MaxNameTableCharCount = ele.ReaderQuotas.MaxNameTableCharCount;
                                wsdual.UseDefaultWebProxy = ele.UseDefaultWebProxy;
                                wsdual.Security.Mode = ele.Security.Mode;
                            }
                            if (serviceTemp == null)
                                _host.AddServiceEndpoint(item.Contract, wsdual, item.Address);
                            else
                                serviceTemp.Binding = wsdual;
                        }
                        #endregion

                        #region WSHttpBinding
                        else if (item.Binding.Equals("WSHttpBinding", StringComparison.OrdinalIgnoreCase))
                        {
                            WSHttpBinding wsdual = new WSHttpBinding();
                            if (_setting.BindingConfig.WSHttpBinding.ContainsKey(item.BindingConfiguration))
                            {
                                WSHttpBindingElement ele = _setting.BindingConfig.WSHttpBinding.Bindings[item.BindingConfiguration];
                                wsdual.OpenTimeout = ele.OpenTimeout;
                                wsdual.CloseTimeout = ele.CloseTimeout;
                                wsdual.SendTimeout = ele.SendTimeout;
                                wsdual.ReceiveTimeout = ele.ReceiveTimeout;
                                wsdual.MaxReceivedMessageSize = ele.MaxReceivedMessageSize;
                                //wsdual.MaxBufferSize = ele.MaxBufferSize;
                                wsdual.MaxBufferPoolSize = ele.MaxBufferPoolSize;
                                wsdual.ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas();
                                wsdual.ReaderQuotas.MaxArrayLength = ele.ReaderQuotas.MaxArrayLength;
                                wsdual.ReaderQuotas.MaxStringContentLength = ele.ReaderQuotas.MaxStringContentLength;
                                wsdual.ReaderQuotas.MaxDepth = ele.ReaderQuotas.MaxDepth;
                                wsdual.ReaderQuotas.MaxBytesPerRead = ele.ReaderQuotas.MaxBytesPerRead;
                                wsdual.ReaderQuotas.MaxNameTableCharCount = ele.ReaderQuotas.MaxNameTableCharCount;
                                wsdual.UseDefaultWebProxy = ele.UseDefaultWebProxy;
                                wsdual.Security.Mode = ele.Security.Mode;
                            }
                            if (serviceTemp == null)
                                _host.AddServiceEndpoint(item.Contract, wsdual, item.Address);
                            else
                                serviceTemp.Binding = wsdual;
                        }
                        #endregion
                    }
                    #endregion

                    #region 行为设置
                    if (_setting.BehaviorConfig.ServiceBehaviors.ContainsKey(service.BehaviorConfiguration))
                    {
                        ServiceBehaviorElement haviorelement = _setting.BehaviorConfig.ServiceBehaviors[service.BehaviorConfiguration];
                        
                        #region 并发设置
                        List<ServiceThrottlingElement> throttlingConfig = haviorelement.OfType<ServiceThrottlingElement>().ToList();
                        ServiceThrottlingBehavior throttlingBehavior = _host.Description.Behaviors.Find<ServiceThrottlingBehavior>();
                        if (null == throttlingBehavior)
                        {
                            throttlingBehavior = new ServiceThrottlingBehavior();
                            _host.Description.Behaviors.Add(throttlingBehavior);
                        }
                        if (throttlingConfig.Count > 0)
                        {
                            //当前ServiceHost能够处理的最大并发消息数量，默认值为16
                            throttlingBehavior.MaxConcurrentCalls = throttlingConfig[0].MaxConcurrentCalls;
                            //当前ServiceHost允许存在的InstanceContext的最大数量，默认值为26
                            throttlingBehavior.MaxConcurrentInstances = throttlingConfig[0].MaxConcurrentInstances;
                            //当前ServiceHost允许的最大并发会话数量，默认值为10
                            throttlingBehavior.MaxConcurrentSessions = throttlingConfig[0].MaxConcurrentSessions;
                        }
                        #endregion

                        #region 序列化最大项
                        ServiceBehaviorAttribute att = new ServiceBehaviorAttribute();
                        att.Name = service.BehaviorConfiguration;
                        if (_host.Description.Behaviors.Find<ServiceBehaviorAttribute>() == null)
                        {
                            _host.Description.Behaviors.Add(att);
                        }
                        else
                        {
                            att = _host.Description.Behaviors.Find<ServiceBehaviorAttribute>();
                        }
                        List<DataContractSerializerElement> serializerConfig = haviorelement.OfType<DataContractSerializerElement>().ToList();
                        if (serializerConfig.Count > 0)
                        {
                            att.MaxItemsInObjectGraph = serializerConfig[0].MaxItemsInObjectGraph;
                        }
                        #endregion

                        #region 是否充许客户端看到详细错误信息
                        List<ServiceDebugElement> debugConfig = haviorelement.OfType<ServiceDebugElement>().ToList();
                        if (debugConfig.Count > 0)
                        {
                            ServiceDebugBehavior debug = new ServiceDebugBehavior();
                            if (_host.Description.Behaviors.Find<ServiceDebugBehavior>() == null)
                            {
                                _host.Description.Behaviors.Add(debug);
                            }
                            else
                            {
                                debug = _host.Description.Behaviors.Find<ServiceDebugBehavior>();
                            }
                            debug.IncludeExceptionDetailInFaults = debugConfig[0].IncludeExceptionDetailInFaults;
                            if (_setting.MetaDataPort>0 && service.Endpoints.Count > 0)
                            {
                                try
                                {
                                    ServiceMetadataBehavior metadata = new ServiceMetadataBehavior();
                                    if (_host.Description.Behaviors.Find<ServiceMetadataBehavior>() == null)
                                    {
                                        _host.Description.Behaviors.Add(metadata);
                                    }
                                    else
                                    {
                                        metadata = _host.Description.Behaviors.Find<ServiceMetadataBehavior>();
                                    }
                                    metadata.HttpGetEnabled = true;
                                    string tempurl = service.Endpoints[0].Address.ToString();
                                    tempurl = tempurl.Substring(tempurl.LastIndexOf(':'));
                                    tempurl = tempurl.Substring(tempurl.IndexOf('/'));
                                    metadata.HttpGetUrl = new Uri(string.Format("http://localhost:{0}{1}/{2}", _setting.MetaDataPort, tempurl, "metadata"));
                                }
                                catch
                                {
                                    _host.Description.Behaviors.Remove<ServiceMetadataBehavior>();
                                }
                            }
                        }
                        #endregion
                    }
                    #endregion
                }
            }
        }
        #endregion

        #region IDisposable 成员

        public void Dispose()
        {
            Close();
            this.Dispose();
        }

        #endregion
    }
    /// <summary>
    /// WCF参数设置类
    /// </summary>
    public class WcfSettingConfig
    {
        #region 属性
        private ClientSection clientconfig = new ClientSection();
        public ClientSection ClientConfig
        {
            get { return clientconfig; }
            set { clientconfig = value; }
        }

        private ServicesSection serviceconfig = new ServicesSection();
        public ServicesSection ServiceConfig
        {
            get { return serviceconfig; }
            set { serviceconfig = value; }
        }

        private BehaviorsSection behaviorconfig = new BehaviorsSection();
        public BehaviorsSection BehaviorConfig
        {
            get { return behaviorconfig; }
            set { behaviorconfig = value; }
        }

        private BindingsSection bindingconfig = new BindingsSection();
        public BindingsSection BindingConfig
        {
            get { return bindingconfig; }
            set { bindingconfig = value; }
        }
        /// <summary>
        /// 实现契约类的类型
        /// </summary>
        internal Type ImplementsContractType
        {
            get;
            private set;
        }

        /// <summary>
        /// 契约类型
        /// </summary>
        internal Type InterfaceContractType
        {
            get;
            private set;
        }

        private bool _isShowErrorInfoToClient = true;
        /// <summary>
        /// 是否显示具体信息到客户端
        /// </summary>
        public bool IsShowErrorInfoToClient
        {
            get { return _isShowErrorInfoToClient; }
            set { _isShowErrorInfoToClient = value; }
        }

        private Uri _ClientBaseAddress;
        /// <summary>
        /// 当启动服务类型为WSDualHttpBinding，且操作系统为XP以下时，则须对该属性赋值
        /// </summary>
        public Uri ClientBaseAddress
        {
            get { return _ClientBaseAddress; }
            set {
                _ClientBaseAddress = value;
                if(bindingconfig.WSDualHttpBinding.Bindings.Count>0)
                bindingconfig.WSDualHttpBinding.Bindings[0].ClientBaseAddress = value;
            }
        }

        InstanceContext instanceContext = null;
        /// <summary>
        /// 客户端回调接口实现对象(双向通信)
        /// </summary>
        public object CallBackObject
        {
            get;
            set;
        }

        private bool _PortSharingEnabled = true;
        /// <summary>
        /// 是否开启端口共享
        /// </summary>
        public bool PortSharingEnabled { 
            get { return _PortSharingEnabled; } 
            set { _PortSharingEnabled = value; } 
        }
        
        /// <summary>
        /// 元数据信息发布端口
        /// </summary>
        public int MetaDataPort { get; set; }
        #endregion

        private void AddService(string serviceName)
        {
            if (serviceconfig.Services.ContainsKey(serviceName)) return;
            ServiceElement item = new ServiceElement();
            item.Name = serviceName;
            item.BehaviorConfiguration = serviceName + "_BehaviorConfiguration";
            serviceconfig.Services.Add(item);
        }
        /// <summary>
        /// 获取启动服务参数
        /// </summary>
        /// <param name="implementsContractType">实体契约类的类型</param>
        /// <param name="interfaceContractType">契约类型</param>
        /// <param name="uri">服务地址</param>
        /// <param name="binding">启动服务类型</param>
        public void GetServiceConfig(Type implementsContractType, Type interfaceContractType, Uri uri, BindingType binding)
        {
            ImplementsContractType = implementsContractType;
            AddService(implementsContractType.ToString());
            ServiceEndpointElement item = new ServiceEndpointElement(uri, interfaceContractType.ToString());
            item.BindingConfiguration = item.Name = interfaceContractType.ToString();
            item.Binding = binding.ToString();

            ServiceElement service = serviceconfig.Services[implementsContractType.ToString()];
            service.Endpoints.Add(item);

            SetBindingParam(uri, binding, item.BindingConfiguration);

            if (!behaviorconfig.ServiceBehaviors.ContainsKey(service.BehaviorConfiguration))
            {
                ServiceBehaviorElement haviorelement = new ServiceBehaviorElement();// _setting.BehaviorConfig.ServiceBehaviors[service.BehaviorConfiguration];
                haviorelement.Name = service.BehaviorConfiguration;
                #region 并发设置
                //List<ServiceThrottlingElement> throttlingConfig = haviorelement.OfType<ServiceThrottlingElement>().ToList();
                ServiceThrottlingElement throttlingBehavior = new ServiceThrottlingElement();// host.Description.Behaviors.Find<ServiceThrottlingBehavior>();
                //当前ServiceHost能够处理的最大并发消息数量，默认值为16
                throttlingBehavior.MaxConcurrentCalls = int.MaxValue;
                //当前ServiceHost允许存在的InstanceContext的最大数量，默认值为26
                throttlingBehavior.MaxConcurrentInstances = int.MaxValue;
                //当前ServiceHost允许的最大并发会话数量，默认值为10
                throttlingBehavior.MaxConcurrentSessions = int.MaxValue;
                //throttlingConfig.Add(throttlingBehavior);
                haviorelement.Add(throttlingBehavior);
                #endregion

                #region 序列化最大项
                DataContractSerializerElement dataContractSerializerElement = new System.ServiceModel.Configuration.DataContractSerializerElement();
                dataContractSerializerElement.MaxItemsInObjectGraph = 2147483647;
                haviorelement.Add(dataContractSerializerElement);
                #endregion

                #region 是否充许客户端看到详细错误信息
                ServiceDebugElement debugConfig = new ServiceDebugElement();
                debugConfig.IncludeExceptionDetailInFaults = _isShowErrorInfoToClient;
                haviorelement.Add(debugConfig);
                #endregion

                behaviorconfig.ServiceBehaviors.Add(haviorelement);
            }
        }
        /// <summary>
        /// 获取启动服务参数
        /// </summary>
        /// <param name="implementsContractType">实体契约类的类型</param>
        /// <param name="interfaceContractType">契约类型</param>
        /// <param name="port">监听端口</param>
        /// <param name="serviceName">服务名称</param>
        /// <param name="binding">启动服务类型</param>
        public void GetServiceConfig(Type implementsContractType, Type interfaceContractType, int port,string serviceName, BindingType binding)
        {
            GetServiceConfig(implementsContractType, interfaceContractType, binding == BindingType.netTcpBinding ? new Uri(string.Format("net.tcp://localhost:{0}/{1}", port, serviceName)) : new Uri(string.Format("http://localhost:{0}/{1}", port, serviceName)), binding);
        }

        private ChannelEndpointElement AddClient(Type interfaceContractType, Uri uri, BindingType binding)
        {
            this.InterfaceContractType = interfaceContractType;
            if (clientconfig.Endpoints.OfType<ChannelEndpointElement>().TakeWhile<ChannelEndpointElement>(p => p.Address.Equals(uri)).Count() == 0)
            {
                ChannelEndpointElement endpoint = new ChannelEndpointElement(new EndpointAddress(uri), ContractDescription.GetContract(interfaceContractType).ToString());
                endpoint.Name = endpoint.BindingConfiguration = endpoint.EndpointConfiguration = endpoint.BehaviorConfiguration = uri.ToString();
                endpoint.Binding = binding.ToString(); endpoint.Contract = interfaceContractType.ToString();
                clientconfig.Endpoints.Add(endpoint);
            }
            return clientconfig.Endpoints.OfType<ChannelEndpointElement>().TakeWhile<ChannelEndpointElement>(p => p.Address.Equals(uri)).ToList()[0];
        }

        /// <summary>
        /// 获取启动客户端参数
        /// </summary>
        /// <param name="interfaceContractType">契约</param>
        /// <param name="uri">服务地址</param>
        /// <param name="binding">启动类型</param>
        public void GetClientConfig(Type interfaceContractType, Uri uri, BindingType binding)
        {
            ChannelEndpointElement item = AddClient(interfaceContractType, uri, binding);

            SetBindingParam(uri, binding, item.BindingConfiguration);


            if (!behaviorconfig.EndpointBehaviors.ContainsKey(item.BehaviorConfiguration))
            {
                EndpointBehaviorElement haviorelement = new EndpointBehaviorElement();// _setting.BehaviorConfig.ServiceBehaviors[service.BehaviorConfiguration];
                haviorelement.Name = item.BehaviorConfiguration;

                #region 序列化最大项
                DataContractSerializerElement dataContractSerializerElement = new System.ServiceModel.Configuration.DataContractSerializerElement();
                dataContractSerializerElement.MaxItemsInObjectGraph = 2147483647;
                haviorelement.Add(dataContractSerializerElement);
                #endregion

                #region 是否充许客户端看到详细错误信息
                CallbackDebugElement debugConfig = new CallbackDebugElement();
                debugConfig.IncludeExceptionDetailInFaults = _isShowErrorInfoToClient;
                haviorelement.Add(debugConfig);
                #endregion

                behaviorconfig.EndpointBehaviors.Add(haviorelement);
            }
        }

        /// <summary>
        /// 设置终结点参数
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="binding"></param>
        /// <param name="key"></param>
        private void SetBindingParam(Uri uri, BindingType binding, string key)
        {
            if (binding == BindingType.netTcpBinding && !bindingconfig.NetTcpBinding.ContainsKey(key))
            {
                NetTcpBindingElement ele = new NetTcpBindingElement();
                ele.OpenTimeout = TimeSpan.FromMinutes(5);
                ele.CloseTimeout = TimeSpan.FromMinutes(5);
                ele.SendTimeout = TimeSpan.FromMinutes(5);
                ele.ReceiveTimeout = TimeSpan.FromMinutes(5);
                ele.MaxReceivedMessageSize = 2147483647;
                ele.MaxBufferSize = 2147483647;
                ele.MaxBufferPoolSize = 2147483647;
                ele.PortSharingEnabled = _PortSharingEnabled;
                ele.ReaderQuotas.MaxArrayLength = 2147483647;
                ele.ReaderQuotas.MaxStringContentLength = 2147483647;
                ele.ReaderQuotas.MaxDepth = 2147483647;
                ele.ReaderQuotas.MaxBytesPerRead = 2147483647;
                ele.ReaderQuotas.MaxNameTableCharCount = 2147483647;
                ele.Security.Mode = SecurityMode.None;
                ele.Name = key;
                bindingconfig.NetTcpBinding.Bindings.Add(ele);
            }
            else if (binding == BindingType.WSDualHttpBinding && !bindingconfig.WSDualHttpBinding.ContainsKey(key))
            {
                WSDualHttpBindingElement ele = new WSDualHttpBindingElement();
                ele.OpenTimeout = TimeSpan.FromMinutes(5);
                ele.CloseTimeout = TimeSpan.FromMinutes(5);
                ele.SendTimeout = TimeSpan.FromMinutes(5);
                ele.ReceiveTimeout = TimeSpan.FromMinutes(5);
                ele.MaxReceivedMessageSize = 2147483647;
                //ele.ClientBaseAddress = uri;
                ele.MaxBufferPoolSize = 2147483647;
                ele.ReaderQuotas.MaxArrayLength = 2147483647; 
                ele.ReaderQuotas.MaxStringContentLength = 2147483647;
                ele.ReaderQuotas.MaxDepth = 2147483647;
                ele.ReaderQuotas.MaxBytesPerRead = 2147483647;
                ele.ReaderQuotas.MaxNameTableCharCount = 2147483647;
                ele.Security.Mode = WSDualHttpSecurityMode.None;
                ele.Name = key;
                ele.UseDefaultWebProxy = false;
                if (ClientBaseAddress != null)
                    ele.ClientBaseAddress = ClientBaseAddress;
                bindingconfig.WSDualHttpBinding.Bindings.Add(ele);
            }
            else if (binding == BindingType.BasicHttpBinding && !bindingconfig.BasicHttpBinding.ContainsKey(key))
            {
                BasicHttpBindingElement ele = new BasicHttpBindingElement();
                ele.OpenTimeout = TimeSpan.FromMinutes(5);
                ele.CloseTimeout = TimeSpan.FromMinutes(5);
                ele.SendTimeout = TimeSpan.FromMinutes(5);
                ele.ReceiveTimeout = TimeSpan.FromMinutes(5);
                ele.MaxReceivedMessageSize = 2147483647;
                ele.MaxBufferSize = 2147483647;
                ele.MaxBufferPoolSize = 2147483647;
                ele.ReaderQuotas.MaxArrayLength = 2147483647;
                ele.ReaderQuotas.MaxStringContentLength = 2147483647;
                ele.ReaderQuotas.MaxDepth = 2147483647;
                ele.ReaderQuotas.MaxBytesPerRead = 2147483647;
                ele.ReaderQuotas.MaxNameTableCharCount = 2147483647;
                ele.Security.Mode = BasicHttpSecurityMode.None;
                ele.Name = key;
                ele.UseDefaultWebProxy = false;
                bindingconfig.BasicHttpBinding.Bindings.Add(ele);
            }
            else if (binding == BindingType.WSHttpBinding && !bindingconfig.WSHttpBinding.ContainsKey(key))
            {
                WSHttpBindingElement ele = new WSHttpBindingElement();
                ele.OpenTimeout = TimeSpan.FromMinutes(5);
                ele.CloseTimeout = TimeSpan.FromMinutes(5);
                ele.SendTimeout = TimeSpan.FromMinutes(5);
                ele.ReceiveTimeout = TimeSpan.FromMinutes(5);
                ele.MaxReceivedMessageSize = 2147483647;
                //ele.MaxBufferSize = 2147483647;
                ele.MaxBufferPoolSize = 2147483647;
                ele.ReaderQuotas.MaxArrayLength = 2147483647;
                ele.ReaderQuotas.MaxStringContentLength = 2147483647;
                ele.ReaderQuotas.MaxDepth = 2147483647;
                ele.ReaderQuotas.MaxBytesPerRead = 2147483647;
                ele.ReaderQuotas.MaxNameTableCharCount = 2147483647;
                ele.Security.Mode = SecurityMode.None;
                ele.Name = key;
                ele.UseDefaultWebProxy = false;
                bindingconfig.WSHttpBinding.Bindings.Add(ele);
            }
        }
        /// <summary>
        /// 清空
        /// </summary>
        public void Clear()
        {
            CallBackObject = null;
            ClientBaseAddress = null;
            _isShowErrorInfoToClient = false;
            MetaDataPort = 0;
            serviceconfig.Services.Clear();
            clientconfig.Endpoints.Clear();
            bindingconfig.BindingCollections.ForEach(delegate(BindingCollectionElement ele) { dynamic e = ele; e.Bindings.Clear(); });
            behaviorconfig.EndpointBehaviors.Clear();
            behaviorconfig.ServiceBehaviors.Clear();
        }
    }
    /// <summary>
    /// 服务启动类型
    /// </summary>
    public enum BindingType
    {
        netTcpBinding,
        WSDualHttpBinding,
        BasicHttpBinding,
        WSHttpBinding
    }
}
