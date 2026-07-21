import requests
import json

try:
    response = requests.get("http://localhost:5244/api/sites")
    if response.status_code == 200:
        sites = response.json()
        for site in sites:
            print(f"ID: {site.get('id')} - Name: {site.get('name')}")
    else:
        print(f"Error: {response.status_code}")
except Exception as e:
    print(f"Error: {e}")
