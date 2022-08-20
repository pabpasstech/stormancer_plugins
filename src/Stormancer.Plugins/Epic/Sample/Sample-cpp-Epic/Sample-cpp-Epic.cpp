#include "Users/ClientAPI.hpp"
#include "Users/Users.hpp"
#include "Party/Party.hpp"
#include "GameFinder/GameFinder.hpp"
#include "stormancer/Logger/ConsoleLogger.h"

#include <thread>

#include "../../cpp/Epic.hpp"

// Copy GameProductConfig.sample.h to GameProductConfig.h with values corresponding to your Epic game product
#include "GameProductConfig.h"

using namespace Stormancer;

int main()
{
	static std::shared_ptr<ILogger> s_logger = std::make_shared<ConsoleLogger>();
	std::shared_ptr<MainThreadActionDispatcher> actionDispatcher = std::make_shared<MainThreadActionDispatcher>();

	auto config = Configuration::create(STORM_ENDPOINT, STORM_ACCOUNT, STORM_APPLICATION);
	config->logger = s_logger;
	config->actionDispatcher = actionDispatcher;
	config->additionalParameters[Epic::ConfigurationKeys::AuthenticationEnabled] = "true";
	config->additionalParameters[Epic::ConfigurationKeys::LoginMode] = STORM_EPIC_LOGIN_MODE;
	config->additionalParameters[Epic::ConfigurationKeys::DevAuthHost] = STORM_EPIC_DEVAUTH_CREDENTIALS_HOST;
	config->additionalParameters[Epic::ConfigurationKeys::DevAuthCredentialsName] = STORM_EPIC_DEVAUTH_CREDENTIALS_NAME;
	config->additionalParameters[Epic::ConfigurationKeys::ProductId] = STORM_EPIC_PRODUCT_ID;
	config->additionalParameters[Epic::ConfigurationKeys::SandboxId] = STORM_EPIC_SANDBOX_ID;
	config->additionalParameters[Epic::ConfigurationKeys::DeploymentId] = STORM_EPIC_DEPLOYMENT_ID;
	config->addPlugin(new Users::UsersPlugin());
	config->addPlugin(new GameFinder::GameFinderPlugin());
	config->addPlugin(new Party::PartyPlugin());
	config->addPlugin(new Epic::EpicPlugin());
	auto client = IClient::create(config);
	auto usersApi = client->dependencyResolver().resolve<Users::UsersApi>();
	auto logger = client->dependencyResolver().resolve<ILogger>();

	bool disconnected = false;

	std::thread mainLoop([actionDispatcher, &disconnected]()
	{
		while (!disconnected)
		{
			actionDispatcher->update(std::chrono::milliseconds(10));
		}
	});

	std::thread thread([usersApi, logger, &disconnected, client]()
	{
		usersApi->login().get();
		std::string userId = usersApi->userId();
		std::string username = usersApi->username();
		logger->log(LogLevel::Info, "SampleMain", "Login succeed!", "userId = " + userId + "; userName = " + username);
		usersApi->logout()
			.then([client, &disconnected]()
		{
			client->disconnect()
				.then([&disconnected]()
			{
				disconnected = true;
			});
		});
	});

	thread.join();
	mainLoop.join();

	return 0;
}