#pragma once
#include "Users/ClientAPI.hpp"
#include "Users/Users.hpp"
#include "stormancer/IPlugin.h"
#include <sstream>
#include <iostream>
#include <vector>

namespace Stormancer
{
	namespace ServerPools
	{
		/// <summary>
		/// Keys to use in Configuration::additionalParameters map to customize the plugin behavior.
		/// </summary>
		namespace ConfigurationKeys
		{
			/// <summary>
			/// Gets the server connection parameters from environment variables.
			/// </summary>
			/// <remarks>
			/// Env variables:
			/// Stormancer.Server.ClusterEndpoints		: Comma separated list of endpoints the server should use to communicate with the Stormancer cluster.
			/// Stormancer.Server.Port					: The local port the server should bind to.
			/// Stormancer.Server.PublishedAddresses	: Comma separated list of public address the players can use to communicate with this server.
			/// Stormancer.Server.PublishedPort			: The public port mapped to the port the client is bound to.
			/// Stormancer.Server.AuthenticationToken	: Server authentication token.
			/// </remarks>
			constexpr const char* GetParametersFromEnv = "server.parameters.fromEnvironmentVariables";
		}

		enum class Status
		{
			Unknown,
			//Server initializing
			Initializing,
			//Server ready to accept a game
			Ready,
			//Game in progress
			InProgress,
			//Game complete
			Complete
		};
		struct Group
		{
			std::string groupId;
			std::vector<std::string> playerIds;

			MSGPACK_DEFINE(groupId, playerIds)
		};
		struct Team
		{
			std::string teamId;
			std::vector<Group> groups;

			MSGPACK_DEFINE(teamId, groups)
		};
		template<typename T>
		struct GameSessionConfiguration
		{
			bool isPublic;
			bool canRestart;
			std::string hostUserId;
			std::vector<Team> teams;
		
			std::vector<std::string> playerIds;

			//parameters is transmitted as a msgpack map. It must therefore use MSGPACK_DEFINE_MAP instead of MSGPACK_DEFINE
			T parameters;

			MSGPACK_DEFINE(isPublic, canRestart, hostUserId, teams, playerIds, parameters )
		};

		template<typename T>
		struct GameSessionStartupParameters
		{
			std::string gameSessionConnectionToken;
			GameSessionConfiguration<T> config;

			MSGPACK_DEFINE(gameSessionConnectionToken, config)
		};

		class ServerPoolsPlugin;

		namespace details
		{
			class ServerPoolConfiguration
			{
			public:
				ServerPoolConfiguration(std::shared_ptr<Stormancer::Configuration> config) :
					_config(config)
				{

				}

				void applyConfig()
				{
					auto it = _config->additionalHeaders.find(ConfigurationKeys::GetParametersFromEnv);
					getConfigFromEnvironmentVariables = it != _config->additionalHeaders.end() && it->second == "true";

					if (getConfigFromEnvironmentVariables)
					{
						/// Stormancer.Server.ClusterEndpoints	: Comma separated list of endpoints the server should use to communicate with the Stormancer cluster.

						auto serverEndpoints = std::getenv("Stormancer.Server.ClusterEndpoints");
						if (serverEndpoints)
						{
							_config->clearServerEndpoints();

							std::istringstream f(serverEndpoints);
							std::string s;
							while (getline(f, s, ',')) {

								_config->addServerEndpoint(s);
							}
						}

						/// Stormancer.Server.Port		: The local port the transport should bind to.
						auto port = std::getenv("Stormancer.Server.Port");

						if (port)
						{
							_config->port = std::atoi(port);
						}

						/// Stormancer.Server.PublishedEndpoint	: The public endpoint the players can use to communicate with this server.
						auto publishedAddresses = std::getenv("Stormancer.Server.PublishedAddresses");
						if (publishedAddresses)
						{
							//If there is a published address, the peer is directly reachable. We disable nat traversal. 
							_config->enableNatPunchthrough = false;

							std::istringstream f(publishedAddresses);
							std::string s;
							while (getline(f, s, ',')) {

								_config->publishedAddresses.push_back(s);
							}
							/// Stormancer.Server.PublishedPort			: The public port mapped to the port the client is bound to.
							auto publishedPort = std::getenv("Stormancer.Server.PublishedPort");

							if (publishedPort)
							{
								_config->publishedPort = std::atoi(publishedPort);
							}
							else
							{
								_config->publishedPort = _config->port;
							}

						}

						/// Stormancer.Server.AuthenticationToken	: Server authentication token.
						auto authToken = std::getenv("Stormancer.Server.AuthenticationToken");

						if (authToken)
						{
							this->authToken = authToken;
						}
					}
				}

			public:
				std::string authToken;
				bool getConfigFromEnvironmentVariables = false;

			private:
				std::shared_ptr<Stormancer::Configuration> _config;
			};
			class ServerPoolsService :public std::enable_shared_from_this<ServerPoolsService>
			{
				friend class ::Stormancer::ServerPools::ServerPoolsPlugin;

			public:
				ServerPoolsService(std::shared_ptr<Stormancer::RpcService> rpc)
					: _rpcService(rpc)

				{
				}

				template<typename T>
				pplx::task<GameSessionStartupParameters<T>> waitGameSession()
				{
					auto rpc = _rpcService.lock();
					if (!rpc)
					{
						throw Stormancer::ObjectDeletedException("RpcService");
					}
					return rpc->rpc<GameSessionStartupParameters<T>>("ServerPool.WaitGameSession");
				}

				//Event fired when Stormancer requests a status update from the server
				std::function<Stormancer::ServerPools::Status()> getStatusCallback;


				Subscription subscribeShutdownReceived(std::function<void()> callback)
				{
					return shutdownReceived.subscribe(callback);
				}

			private:

				std::weak_ptr<Stormancer::RpcService> _rpcService;

				//Initializes the service
				void initialize(std::shared_ptr<Stormancer::Scene> scene)
				{
					//Capture a weak pointer of this in the route handler to make sure that:
					//* We don't prevent this from being destroyed (capturing a shared pointer)
					//* If destroyed, we don't try to use it in the handler (capturing a this reference directly)
					std::weak_ptr<ServerPoolsService> wService = this->shared_from_this();
					scene->addRoute("ServerPool.Shutdown", [wService](Stormancer::Packetisp_ptr)
						{
							auto service = wService.lock();
							//If service is valid, forward the event.
							if (service)
							{
								service->shutdownReceived();
							}
						});

					auto rpc = _rpcService.lock();

					rpc->addProcedure("ServerPool.GetStatus", [wService](Stormancer::RpcRequestContext_ptr ctx)
						{
							auto service = wService.lock();
							if (!service)
							{
								ctx->sendValueTemplated(Stormancer::ServerPools::Status::Complete);
							}
							if (!service->getStatusCallback)
							{
								ctx->sendValueTemplated(Stormancer::ServerPools::Status::Unknown);
							}
							return pplx::task_from_result();
						});
				}

				//Event fired when the service client receives a shutdown request
				Stormancer::Event<> shutdownReceived;

			};

			class ServerAuthenticationHandler : public Stormancer::Users::IAuthenticationEventHandler
			{
			public:
				ServerAuthenticationHandler(std::shared_ptr<ServerPoolConfiguration> config, std::shared_ptr<ILogger> logger) 
					: _config(config)
					,_logger(logger)
				{

				}
				virtual pplx::task<void> retrieveCredentials(const Stormancer::Users::CredentialsContext& ctx)
				{
					if (_config->authToken.size() > 0)
					{
						_logger->log(LogLevel::Info, "auth.dedicatedServer", "Auth token found. Authenticated as a game server");
						ctx.authParameters->type = "gameServer";
						ctx.authParameters->parameters["token"] = _config->authToken;
					}
					else
					{
						_logger->log(LogLevel::Warn, "auth.dedicatedServer", "No auth token found. Auth disabled, trying to connect as an unauthenticated development game server. ");

						ctx.authParameters->type = "gameServer.dev";
					}
					return pplx::task_from_result();
				}

			private:
				std::shared_ptr<ServerPoolConfiguration> _config;
				std::shared_ptr<ILogger> _logger;
			};
		}

		class ServerPools : public Stormancer::ClientAPI<ServerPools, details::ServerPoolsService>
		{
			friend class ServerPoolsPlugin;

		public:

			ServerPools(std::weak_ptr<Stormancer::Users::UsersApi> auth)
				: Stormancer::ClientAPI<ServerPools, details::ServerPoolsService>(auth, "stormancer.plugins.serverPool")
			{
			}

			template<typename T>
			pplx::task<GameSessionStartupParameters<T>> waitGameSession()
			{
				return this->getService()
					.then([](std::shared_ptr<details::ServerPoolsService> service)
						{
							return service->waitGameSession<T>();
						});
			}

			Subscription subscribeShutdownReceived(std::function<void()> callback)
			{
				return shutdownReceived.subscribe(callback);
			}

			void setGetStatusCallback(std::function<Stormancer::ServerPools::Status()> callback)
			{
				getStatusCallback = callback;
			}


		private:

			void onConnecting(std::shared_ptr <details::ServerPoolsService> service)
			{
				std::weak_ptr<ServerPools> wThis = this->shared_from_this();
				//Always capture weak references, and NEVER 'this'. As the callback is going to be executed asynchronously,
				//who knows what may have happened to the object behind the this pointer since it was captured?
				shutdownReceivedSubscription = service->subscribeShutdownReceived([wThis]()
					{
						auto that = wThis.lock();
						//If this is valid, forward the event.
						if (that)
						{
							that->shutdownReceived();
						}
					});
				service->getStatusCallback = [wThis]()
				{
					auto that = wThis.lock();
					if (that && that->getStatusCallback)
					{
						return that->getStatusCallback();
					}
					else
					{
						return Stormancer::ServerPools::Status::Unknown;
					}
				};
			}

			void onDisconnecting(std::shared_ptr <details::ServerPoolsService>)
			{
				//Unsubscribe by destroying the subscription
				shutdownReceivedSubscription = nullptr;
			}

			Stormancer::Event<std::string>::Subscription shutdownReceivedSubscription;
			//Event fired when Stormancer requests a status update from the server
			std::function<Stormancer::ServerPools::Status()> getStatusCallback;

			Stormancer::Event<> shutdownReceived;
		};

		class ServerPoolsPlugin : public Stormancer::IPlugin
		{
		public:

			static constexpr const char* PLUGIN_NAME = "ServerPools";
			static constexpr const char* PLUGIN_VERSION = "1.0.0";

			PluginDescription getDescription() override
			{
				return PluginDescription(PLUGIN_NAME, PLUGIN_VERSION);
			}

		private:

			void registerSceneDependencies(Stormancer::ContainerBuilder& builder, std::shared_ptr<Stormancer::Scene> scene) override
			{
				auto name = scene->getHostMetadata("stormancer.serverPool");

				if (!name.empty())
				{
					builder.registerDependency<details::ServerPoolsService, RpcService>().singleInstance();
				}
			}

			void registerClientDependencies(Stormancer::ContainerBuilder& builder) override
			{
				builder.registerDependency<ServerPools, Stormancer::Users::UsersApi>().singleInstance();
				builder.registerDependency<details::ServerPoolConfiguration, Stormancer::Configuration>().singleInstance();
				builder.registerDependency<details::ServerAuthenticationHandler, details::ServerPoolConfiguration, Stormancer::ILogger>().as<Stormancer::Users::IAuthenticationEventHandler>();
			}

			void sceneCreated(std::shared_ptr<Scene> scene) override
			{
				auto name = scene->getHostMetadata("stormancer.serverPool");

				if (!name.empty())
				{
					auto service = scene->dependencyResolver().resolve<details::ServerPoolsService>();
					service->initialize(scene);
				}
			}

			void sceneConnecting(std::shared_ptr<Scene> scene) override
			{
				auto name = scene->getHostMetadata("stormancer.serverPool");

				if (!name.empty())
				{
					auto pools = scene->dependencyResolver().resolve<ServerPools>();
					auto service = scene->dependencyResolver().resolve<details::ServerPoolsService>();
					pools->onConnecting(service);
				}
			}

			void sceneDisconnecting(std::shared_ptr<Scene> scene) override
			{
				auto name = scene->getHostMetadata("stormancer.serverPool");

				if (!name.empty())
				{
					auto pools = scene->dependencyResolver().resolve<ServerPools>();
					auto service = scene->dependencyResolver().resolve<details::ServerPoolsService>();
					pools->onDisconnecting(service);
				}
			}

			void clientCreating(std::shared_ptr<IClient> client) override
			{
				auto config = client->dependencyResolver().resolve<details::ServerPoolConfiguration>();

				//Applies the plugin config to the client configuration.
				config->applyConfig();


			}

			void clientCreated(std::shared_ptr<IClient> client) override
			{

			}
		};
	}
}

MSGPACK_ADD_ENUM(Stormancer::ServerPools::Status);