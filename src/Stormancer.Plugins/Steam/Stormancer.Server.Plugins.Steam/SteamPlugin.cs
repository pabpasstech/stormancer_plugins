// MIT License
//
// Copyright (c) 2019 Stormancer
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer.Server.Plugins.Configuration;
using Stormancer.Server.Plugins.Friends;
using Stormancer.Server.Plugins.Party;
using Stormancer.Server.Plugins.Profile;
using Stormancer.Server.Plugins.ServiceLocator;
using Stormancer.Server.Plugins.Steam;
using Stormancer.Server.Plugins.Users;
using System.Threading.Tasks;

namespace Stormancer.Server.Plugins.Steam
{
    class SteamPlugin : IHostPlugin
    {
        internal const string METADATA_KEY = "stormancer.plugins.steam";

        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostDependenciesRegistration += (IDependencyBuilder builder) =>
            {
                builder.Register<SteamController>().InstancePerRequest();
                builder.Register<SteamPartyController>().InstancePerRequest();
                builder.Register<SteamProfilePartBuilder>().As<IProfilePartBuilder>();
                builder.Register<SteamService>().As<ISteamService>();
                builder.Register<SteamKeyStore>().As<IConfigurationChangedEventHandler>().AsSelf().SingleInstance();
                builder.Register<SteamFriendsEventHandler>().As<IFriendsEventHandler>();
                builder.Register<SteamPartyEventHandler>().As<IPartyEventHandler>().InstancePerRequest();
                builder.Register<SteamServiceLocator>().As<IServiceLocatorProvider>();
            };

            ctx.SceneDependenciesRegistration += (IDependencyBuilder builder, ISceneHost scene) =>
            {
                if (scene.Template == Constants.SCENE_TEMPLATE)
                {
                    builder.Register<SteamAuthenticationProvider>().As<IAuthenticationProvider>();
                }
            };

            ctx.SceneCreating += (ISceneHost scene) =>
            {
                if (scene.Template == Constants.SCENE_TEMPLATE)
                {
                    scene.Metadata[METADATA_KEY] = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                }
            };

            ctx.SceneCreated += (ISceneHost scene) =>
            {
                if (scene.Template == Constants.SCENE_TEMPLATE)
                {
                    scene.AddController<SteamController>();
                    
                }

                if (scene.Metadata.ContainsKey(PartyConstants.METADATA_KEY))
                {
                    scene.AddController<SteamPartyController>();
                }
            };
        }
    }


    internal class SteamServiceLocator : IServiceLocatorProvider
    {
        public Task LocateService(ServiceLocationCtx ctx)
        {
            if (ctx.ServiceType == "stormancer.steam")
            {
                ctx.SceneId = Constants.GetSceneId();
            }
            return Task.CompletedTask;
        }
    }
}

