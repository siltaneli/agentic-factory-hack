# Get your Entra ID (AAD) user object ID
ME_OBJECT_ID="$(az ad signed-in-user show --query id -o tsv)"

# Assign "Azure AI Developer" at the AI Foundry Project resource scope
az role assignment create \
  --assignee-object-id "$ME_OBJECT_ID" \
  --assignee-principal-type User \
  --role "Azure AI Developer" \
  --scope "$AZURE_AI_PROJECT_RESOURCE_ID"

# Assign "Azure AI User" at the AI Services resource scope
# This is required for agent operations (create/run agents)
# Derive the AI Services ID from the project resource ID
AI_SERVICES_ID="${AZURE_AI_PROJECT_RESOURCE_ID%/projects/*}"

az role assignment create \
  --assignee-object-id "$ME_OBJECT_ID" \
  --assignee-principal-type User \
  --role "Azure AI User" \
  --scope "$AI_SERVICES_ID"

# Assign "Cognitive Services OpenAI Contributor" at the Azure OpenAI resource scope
# This is required for data-plane calls like: /openai/deployments/{deployment}/chat/completions
OPENAI_RESOURCE_ID="$(az cognitiveservices account show --name "$AZURE_OPENAI_SERVICE_NAME" --resource-group "$RESOURCE_GROUP" --query id -o tsv)"

az role assignment create \
  --assignee-object-id "$ME_OBJECT_ID" \
  --assignee-principal-type User \
  --role "Cognitive Services OpenAI Contributor" \
  --scope "$OPENAI_RESOURCE_ID"

# Refresh your credentials with the new permissions
az login --use-device-code