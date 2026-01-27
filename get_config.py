import json
import os
import requests

# Hardcoded from datos.txt
SUPABASE_URL = "https://ycjrtxkhpjqlfvjfbtcn.supabase.co"
SUPABASE_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InljanJ0eGtocGpxbGZ2amZidGNuIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc2ODk0ODkwNywiZXhwIjoyMDg0NTI0OTA3fQ.1BCSSXRJv-Mbq1ZYMwlcliVRnFJBR4kCpTHcV16Ebyg"

headers = {
    "apikey": SUPABASE_KEY,
    "Authorization": f"Bearer {SUPABASE_KEY}",
    "Content-Type": "application/json"
}

response = requests.get(f"{SUPABASE_URL}/rest/v1/config_sites?name=eq.Festo", headers=headers)
if response.status_code == 200:
    data = response.json()
    print(json.dumps(data, indent=2))
else:
    print(f"Error: {response.status_code} {response.text}")
