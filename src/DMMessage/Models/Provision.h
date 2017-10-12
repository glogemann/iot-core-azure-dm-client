#pragma once
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
#pragma once
#include "IRequestIResponse.h"
#include "SerializationHelper.h"
#include "DMMessageKind.h"
#include "StatusCodeResponse.h"
#include "Blob.h"

#include <collection.h>

using namespace Platform;
using namespace Platform::Metadata;
using namespace Platform::Collections;
using namespace Windows::Data::Json;
using namespace Windows::Foundation::Collections;

namespace Microsoft { namespace Devices { namespace Management { namespace Message
{
    public ref class ProvisionInfo sealed
    {
    public:
		ProvisionInfo()
        {
			ProvisioningPkgs = ref new Vector<String^>();
        }
		ProvisionInfo(IVector<String^>^ ppkgs)
        {
			ProvisioningPkgs = ppkgs;
        }
        property IVector<String^>^ ProvisioningPkgs;
    };

    public ref class ProvisionRequest sealed : public IRequest
    {
		ProvisionInfo^ provInfo;
    public:
		ProvisionRequest(ProvisionInfo^ provInfo) : provInfo(provInfo) {}

        virtual Blob^ Serialize() {
            JsonObject^ jsonObject = ref new JsonObject();
            JsonArray^ jsonArray = ref new JsonArray();
            for each (auto dep in provInfo->ProvisioningPkgs)
            {
                jsonArray->Append(JsonValue::CreateStringValue(dep));
            }
            jsonObject->Insert("ProvisioningPkgs", jsonArray);

            return SerializationHelper::CreateBlobFromJson((uint32_t)Tag, jsonObject);
        }

        static IDataPayload^ Deserialize(Blob^ bytes) {
            String^ str = SerializationHelper::GetStringFromBlob(bytes);
            JsonObject^ jsonObject = JsonObject::Parse(str);
            auto dependencies = jsonObject->Lookup("ProvisioningPkgs")->GetArray();
            auto ppkgVector = ref new Vector<String^>();
            for each (auto dep in dependencies)
            {
				ppkgVector->Append(dep->GetString());
            }
            auto appInfo = ref new Microsoft::Devices::Management::Message::ProvisionInfo(ppkgVector);
            return ref new ProvisionRequest(appInfo);
        }

        virtual property DMMessageKind Tag {
            DMMessageKind get();
        }

        property ProvisionInfo^ ProvisionInfo {
            Microsoft::Devices::Management::Message::ProvisionInfo^ get() { return provInfo; }
        }
    };
}}}}
