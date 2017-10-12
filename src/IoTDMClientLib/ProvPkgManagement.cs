/*
Copyright 2017 Microsoft
Permission is hereby granted, free of charge, to any person obtaining a copy of this software 
and associated documentation files (the "Software"), to deal in the Software without restriction, 
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, 
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, 
subject to the following conditions:

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using Microsoft.Devices.Management;
using Microsoft.Devices.Management.Message;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IoTDMClient
{
    internal class ProvisionBlobInfo
    {
        public List<BlobInfo> ProvisioningPackages { get; set; }

        public async Task<string> ProvisionPkgsAsync(DeviceManagementClient client)
        {
            try
            {
                var provInfo = new ProvisionInfo();
                if (null != ProvisioningPackages)
                {
                    foreach (var ppkgBlobInfo in ProvisioningPackages)
                    {
                        var ppkgPath = await ppkgBlobInfo.DownloadToTempAsync(client);
                        provInfo.ProvisioningPkgs.Add(ppkgPath);
                    }
                }

                await client.ProvisionAsync(provInfo);

                var response = JsonConvert.SerializeObject(new { response = "succeeded" });
                return response;
            }
            catch (Exception e)
            {
                var response = JsonConvert.SerializeObject(new { response = "failed", reason = e.Message });
                return response;
            }

        }
    }
}
