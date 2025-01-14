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

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Stormancer.Plugins;
using System.Collections.Generic;

namespace Stormancer.Server.Plugins.AdminApi
{
    class AdminApiPlugin : IHostPlugin
    {
        public void Build(HostPluginBuildContext ctx)
        {
            ctx.HostStarting += (Stormancer.Server.IHost host) =>
            {
                //Configure 
                host.AddAdminApiConfiguration((app, env, scene) =>
                {
                    System.Diagnostics.Debug.WriteLine("start admin swagger");
                    app.UseSwagger();
                    app.UseSwaggerUI(c =>
                    {
                        c.SwaggerEndpoint("v3/swagger.json", "Stormancer Admin Web API V3");
                        
                    });

                    app.UseRouting();

                    app.UseCors(option => option
                       .AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader());

                    app.UseAuthentication();
                    app.UseAuthorization();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                    });
                }, (services, scene) =>
                {
                    services.AddLocalization();
                    var configs = scene.DependencyResolver.Resolve<IEnumerable<IAdminWebApiConfig>>();

                    services.AddMvc(options =>
                    {
                    })
                        .ConfigureApplicationPartManager(apm =>
                        {
                            foreach (var config in configs)
                            {
                                config.ConfigureApplicationParts(apm);
                            }
                        })
                        .AddNewtonsoftJson()
                        .AddControllersAsServices();
  
                    services.AddSwaggerGen(c =>
                    {
                        
                        c.SwaggerDoc("v3", new OpenApiInfo { Title = "Stormancer Admin web API", Version = "v3"    });
                    });
                    services.AddSwaggerGenNewtonsoftSupport();
                });

                host.AddWebApiConfiguration((app, env, scene) =>
                {
                    app.UseSwagger();
                    app.UseSwaggerUI(c =>
                    {
                        c.SwaggerEndpoint("v3/swagger.json", "Stormancer public Web API V3");

                    });
                    app.UseRouting();

                    app.UseCors(option => option
                       .AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader());

                    app.UseAuthentication();
                    app.UseAuthorization();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                    });
                }, (services, scene) =>
                {
                    services.AddLocalization();
                    var configs = scene.DependencyResolver.Resolve<IEnumerable<IPublicWebApiConfig>>();

                    services.AddMvc(options =>
                    {
                    })
                        .ConfigureApplicationPartManager(apm =>
                        {
                            foreach (var config in configs)
                            {
                                config.ConfigureApplicationParts(apm);
                            }
                        })
                        .AddNewtonsoftJson()
                        .AddControllersAsServices();


                    services.AddSwaggerGen(c =>
                    {

                        c.SwaggerDoc("v3", new OpenApiInfo { Title = "Application public web API", Version = "v3" });
                    });

                });
            };
        }
    }
}
